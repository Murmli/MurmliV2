import 'dart:async';
import 'dart:convert';

import 'package:clock/clock.dart';
import 'package:crypto/crypto.dart';
import 'package:dio/dio.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:google_sign_in_platform_interface/google_sign_in_platform_interface.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:sign_in_with_apple_platform_interface/sign_in_with_apple_platform_interface.dart';
import 'package:talker/talker.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/api/failed_api_request_trace_id_provider.dart';
import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/logging/talker_provider.dart';
import 'package:app/app/core/storage/preference_keys.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/google_sign_in.dart';
import '../../../helpers/http_client_adapter.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';
import '../../../helpers/sign_in_with_apple.dart';

void main() {
  late ProviderContainer container;
  final signInTime = DateTime.utc(2026, 11, 22, 12);
  late DateTime now;

  Future<SignInResult> signInWithApple() =>
      container.read(signedInAccountProvider.notifier).signInWithApple();

  Future<SignInResult> signInWithGoogle() =>
      container.read(signedInAccountProvider.notifier).signInWithGoogle();

  Future<SignInResult> signInWithEmail() => container
      .read(signedInAccountProvider.notifier)
      .signInWithEmail(email: signInEmail, password: signInPassword);

  String sentNonce(RequestOptions socialSignIn) =>
      ((socialSignIn.data as Map<String, Object?>)['idToken']!
          as Map<String, String>)['nonce']!;

  late Map<String, String> secureStorage;

  ProviderContainer createDevelopmentContainer(
    SharedPreferencesWithCache preferences,
  ) => createContainer(
    preferences,
    flavor: Flavor.development,
    clock: Clock(() => now),
    secureStorage: secureStorage,
  );

  setUp(() async {
    now = signInTime;
    secureStorage = {};
    container = createDevelopmentContainer(
      await createPreferences({PreferenceKeys.installed: true}),
    );
  });

  Future<void> restore() =>
      container.read(signedInAccountProvider.notifier).restore();

  Future<ResponseBody> respondWithTokenIssuedNow(
    RequestOptions request,
  ) async => request.uri.path == tokenPath
      ? jsonResponse({'token': jsonWebTokenIssuedAt(now)})
      : await respondAsAuth(request);

  Iterable<RequestOptions> tokenRequests(RecordingHttpClientAdapter auth) =>
      auth.requests.where((request) => request.uri.path == tokenPath);

  Iterable<RequestOptions> exampleRequests(RecordingHttpClientAdapter api) =>
      api.requests.where((request) => request.uri.path == '/v1/example');

  Future<Response<void>> requestExample() =>
      container.read(apiClientProvider).get<void>('/v1/example');

  group('SignedInAccount', () {
    test('signInWithApple requests no scopes, signs in with the identity token and the hashed nonce, exchanges the session token for a JSON web token and fetches the account with it', () async {
      final apple = installFakeSignInWithApple();
      final requests = recordAuthenticationRequests(container);

      final result = await signInWithApple();

      expect(result, SignInResult.signedIn);
      expect(container.read(signedInAccountProvider)?.id, accountId);
      expect(apple.receivedScopes, isEmpty);
      final [socialSignIn, tokenRequest] = requests.auth.requests;
      expect(socialSignIn.method, 'POST');
      expect(socialSignIn.uri.path, socialSignInPath);
      final rawNonce = sentNonce(socialSignIn);
      expect(socialSignIn.data, {
        'provider': 'apple',
        'idToken': {'token': appleIdentityToken, 'nonce': rawNonce},
      });
      expect(
        apple.receivedNonce,
        sha256.convert(utf8.encode(rawNonce)).toString(),
      );
      expect(tokenRequest.uri.path, tokenPath);
      expect(
        tokenRequest.headers['Authorization'],
        'Bearer $signedSessionToken',
      );
      final accountRequest = requests.api.requests.single;
      expect(accountRequest.uri.path, '/v1/users/me');
      expect(
        accountRequest.headers['Authorization'],
        'Bearer $issuedJsonWebToken',
      );
    });

    test('signInWithApple canceled by the user returns canceled without requests and stays signed out', () async {
      installFakeSignInWithApple(
        exception: const SignInWithAppleAuthorizationException(
          code: AuthorizationErrorCode.canceled,
          message: 'The user canceled the authorization attempt.',
        ),
      );
      final requests = recordAuthenticationRequests(container);

      final result = await signInWithApple();

      expect(result, SignInResult.canceled);
      expect(requests.auth.requests, isEmpty);
      expect(container.read(signedInAccountProvider), isNull);
    });

    for (final appleFailure in <Exception>[
      const SignInWithAppleAuthorizationException(
        code: AuthorizationErrorCode.failed,
        message: 'The authorization attempt failed.',
      ),
      UnknownSignInWithAppleException(
        platformException: PlatformException(code: 'unexpected'),
      ),
    ]) {
      test(
        'signInWithApple failing with ${appleFailure.runtimeType} returns failed without requests and stays signed out',
        () async {
          installFakeSignInWithApple(exception: appleFailure);
          final requests = recordAuthenticationRequests(container);

          final result = await signInWithApple();

          expect(result, SignInResult.failed);
          expect(requests.auth.requests, isEmpty);
          expect(container.read(signedInAccountProvider), isNull);
        },
      );
    }

    for (final (scenario, respondFromAuth) in [
      (
        'auth rejecting the identity token',
        (RequestOptions request) async => ResponseBody.fromString('', 401),
      ),
      (
        'auth answering without set-auth-token',
        (RequestOptions request) async => request.uri.path == socialSignInPath
            ? jsonResponse({'token': 'unsigned-session-token'})
            : await respondAsAuth(request),
      ),
      (
        'a failed token request',
        (RequestOptions request) async => request.uri.path == tokenPath
            ? ResponseBody.fromString('', 500)
            : await respondAsAuth(request),
      ),
    ]) {
      test(
        'signInWithApple with $scenario returns failed without a request to the API, stays signed out and logs a warning without tokens',
        () async {
          installFakeSignInWithApple();
          final requests = recordAuthenticationRequests(
            container,
            respondFromAuth: respondFromAuth,
          );

          final result = await signInWithApple();

          expect(result, SignInResult.failed);
          expect(requests.api.requests, isEmpty);
          expect(container.read(signedInAccountProvider), isNull);
          final log = container.read(talkerProvider).history.single;
          expect(log.logLevel, LogLevel.warning);
          expect(
            log.message,
            isNot(
              anyOf(
                contains(appleIdentityToken),
                contains('session-token'),
                contains(sentNonce(requests.auth.requests.first)),
              ),
            ),
          );
        },
      );
    }

    test('signInWithApple with a failed request to the API returns apiRequestFailed and stays signed out', () async {
      installFakeSignInWithApple();
      recordAuthenticationRequests(
        container,
        respondFromApi: (_) async => ResponseBody.fromString('', 500),
      );

      final result = await signInWithApple();

      expect(result, SignInResult.apiRequestFailed);
      expect(container.read(signedInAccountProvider), isNull);
      expect(secureStorage, isEmpty);
    });

    test('signInWithApple with the API answering without an account id returns failed and stays signed out', () async {
      installFakeSignInWithApple();
      recordAuthenticationRequests(
        container,
        respondFromApi: (_) async => jsonResponse(<String, Object?>{}),
      );

      final result = await signInWithApple();

      expect(result, SignInResult.failed);
      expect(container.read(signedInAccountProvider), isNull);
      expect(secureStorage, isEmpty);
    });

    test('signInWithGoogle initializes with the development server client ID, signs in with the ID token, exchanges the session token for a JSON web token and fetches the account with it', () async {
      final google = installFakeGoogleSignIn();
      final requests = recordAuthenticationRequests(container);

      final result = await signInWithGoogle();

      expect(result, SignInResult.signedIn);
      expect(container.read(signedInAccountProvider)?.id, accountId);
      expect(
        google.receivedInitParameters.single.serverClientId,
        '919671347923-6osa4hn7cc3f8trnbdb8s826i0jfv63b.apps.googleusercontent.com',
      );
      final [socialSignIn, tokenRequest] = requests.auth.requests;
      expect(socialSignIn.method, 'POST');
      expect(socialSignIn.uri.path, socialSignInPath);
      expect(socialSignIn.data, {
        'provider': 'google',
        'idToken': {'token': googleIdToken},
      });
      expect(tokenRequest.uri.path, tokenPath);
      expect(
        tokenRequest.headers['Authorization'],
        'Bearer $signedSessionToken',
      );
      final accountRequest = requests.api.requests.single;
      expect(accountRequest.uri.path, '/v1/users/me');
      expect(
        accountRequest.headers['Authorization'],
        'Bearer $issuedJsonWebToken',
      );
    });

    test('signInWithGoogle canceled by the user returns canceled without requests and stays signed out', () async {
      installFakeGoogleSignIn(
        exception: const GoogleSignInException(
          code: GoogleSignInExceptionCode.canceled,
        ),
      );
      final requests = recordAuthenticationRequests(container);

      final result = await signInWithGoogle();

      expect(result, SignInResult.canceled);
      expect(requests.auth.requests, isEmpty);
      expect(container.read(signedInAccountProvider), isNull);
    });

    const googleErrorDescription =
        'Account google-user@murmli.de is not allowed';
    const googleErrorDetails = 'developer console project 919671347923';
    for (final (googleFailure, errorCode) in <(Exception, String)>[
      (
        const GoogleSignInException(
          code: GoogleSignInExceptionCode.clientConfigurationError,
          description: googleErrorDescription,
          details: googleErrorDetails,
        ),
        'clientConfigurationError',
      ),
      (
        PlatformException(
          code: 'channel-error',
          message: googleErrorDescription,
          details: googleErrorDetails,
        ),
        'channel-error',
      ),
    ]) {
      test(
        'signInWithGoogle failing with ${googleFailure.runtimeType} $errorCode returns failed without requests, stays signed out and logs a warning with only the error code',
        () async {
          installFakeGoogleSignIn(exception: googleFailure);
          final requests = recordAuthenticationRequests(container);

          final result = await signInWithGoogle();

          expect(result, SignInResult.failed);
          expect(requests.auth.requests, isEmpty);
          expect(container.read(signedInAccountProvider), isNull);
          final log = container.read(talkerProvider).history.single;
          expect(log.logLevel, LogLevel.warning);
          expect(log.message, contains(errorCode));
          expect(
            log.message,
            isNot(
              anyOf(
                contains(googleErrorDescription),
                contains(googleErrorDetails),
              ),
            ),
          );
        },
      );
    }

    test('signInWithGoogle without an ID token returns failed without requests and stays signed out', () async {
      installFakeGoogleSignIn(idToken: null);
      final requests = recordAuthenticationRequests(container);

      final result = await signInWithGoogle();

      expect(result, SignInResult.failed);
      expect(requests.auth.requests, isEmpty);
      expect(container.read(signedInAccountProvider), isNull);
    });

    test('signInWithGoogle called again after a cancel initializes Google sign-in only once', () async {
      final google = installFakeGoogleSignIn(
        exception: const GoogleSignInException(
          code: GoogleSignInExceptionCode.canceled,
        ),
      );

      await signInWithGoogle();
      await signInWithGoogle();

      expect(google.receivedInitParameters, hasLength(1));
      expect(google.authenticateCalls, 2);
    });

    test('signInWithEmail posts email and password, exchanges the session token for a JSON web token and fetches the account with it', () async {
      final requests = recordAuthenticationRequests(container);

      final result = await signInWithEmail();

      expect(result, SignInResult.signedIn);
      expect(container.read(signedInAccountProvider)?.id, accountId);
      final [emailSignIn, tokenRequest] = requests.auth.requests;
      expect(emailSignIn.method, 'POST');
      expect(emailSignIn.uri.path, emailSignInPath);
      expect(emailSignIn.data, {
        'email': signInEmail,
        'password': signInPassword,
      });
      expect(tokenRequest.uri.path, tokenPath);
      expect(
        tokenRequest.headers['Authorization'],
        'Bearer $signedSessionToken',
      );
      final accountRequest = requests.api.requests.single;
      expect(accountRequest.uri.path, '/v1/users/me');
      expect(
        accountRequest.headers['Authorization'],
        'Bearer $issuedJsonWebToken',
      );
    });

    for (final (scenario, respondFromAuth, expectedResult) in [
      (
        'auth rejecting the credentials with 401',
        (RequestOptions request) async => ResponseBody.fromString('', 401),
        SignInResult.invalidCredentials,
      ),
      (
        'auth rejecting a malformed email with 400',
        (RequestOptions request) async => ResponseBody.fromString('', 400),
        SignInResult.invalidCredentials,
      ),
      (
        'auth failing with 500',
        (RequestOptions request) async => ResponseBody.fromString('', 500),
        SignInResult.failed,
      ),
      (
        'auth answering without set-auth-token',
        (RequestOptions request) async =>
            jsonResponse({'token': 'unsigned-session-token'}),
        SignInResult.failed,
      ),
    ]) {
      test(
        'signInWithEmail with $scenario returns ${expectedResult.name} without a request to the API, stays signed out and logs a warning without email and password',
        () async {
          final requests = recordAuthenticationRequests(
            container,
            respondFromAuth: respondFromAuth,
          );

          final result = await signInWithEmail();

          expect(result, expectedResult);
          expect(requests.api.requests, isEmpty);
          expect(container.read(signedInAccountProvider), isNull);
          final log = container.read(talkerProvider).history.single;
          expect(log.logLevel, LogLevel.warning);
          expect(
            log.message,
            isNot(anyOf(contains(signInEmail), contains(signInPassword))),
          );
        },
      );
    }

    test('API request 13 minutes after sign-in reuses the access token of the sign-in', () async {
      final requests = recordAuthenticationRequests(
        container,
        respondFromAuth: respondWithTokenIssuedNow,
      );
      await signInWithEmail();
      now = signInTime.add(const Duration(minutes: 13));

      await requestExample();

      expect(tokenRequests(requests.auth), hasLength(1));
      expect(
        exampleRequests(requests.api).single.headers['Authorization'],
        'Bearer ${jsonWebTokenIssuedAt(signInTime)}',
      );
    });

    test('API request 14 minutes after sign-in renews the access token first and sends the new one', () async {
      final requests = recordAuthenticationRequests(
        container,
        respondFromAuth: respondWithTokenIssuedNow,
      );
      await signInWithEmail();
      now = signInTime.add(const Duration(minutes: 14));

      await requestExample();

      expect(tokenRequests(requests.auth), hasLength(2));
      expect(
        tokenRequests(requests.auth).last.headers['Authorization'],
        'Bearer $signedSessionToken',
      );
      expect(
        exampleRequests(requests.api).single.headers['Authorization'],
        'Bearer ${jsonWebTokenIssuedAt(now)}',
      );
    });

    test(
      'parallel API requests with a due access token share one renewal',
      () async {
        final requests = recordAuthenticationRequests(
          container,
          respondFromAuth: respondWithTokenIssuedNow,
        );
        await signInWithEmail();
        now = signInTime.add(const Duration(minutes: 14));

        await Future.wait([
          requestExample(),
          requestExample(),
          requestExample(),
        ]);

        expect(tokenRequests(requests.auth), hasLength(2));
        expect(
          exampleRequests(requests.api)
              .map((request) => request.headers['Authorization']),
          everyElement('Bearer ${jsonWebTokenIssuedAt(now)}'),
        );
      },
    );

    test('API request rejected with 401 renews the access token once, retries with the new one and returns the response without reporting a failure', () async {
      var exampleResponses = 0;
      final requests = recordAuthenticationRequests(
        container,
        respondFromAuth: respondWithTokenIssuedNow,
        respondFromApi: (request) async => request.uri.path == '/v1/example'
            ? ResponseBody.fromString('', exampleResponses++ == 0 ? 401 : 204)
            : await respondAsApi(request),
      );
      await signInWithEmail();
      now = signInTime.add(const Duration(minutes: 1));

      final response = await requestExample();

      expect(response.statusCode, 204);
      expect(tokenRequests(requests.auth), hasLength(2));
      expect(
        exampleRequests(requests.api)
            .map((request) => request.headers['Authorization']),
        [
          'Bearer ${jsonWebTokenIssuedAt(signInTime)}',
          'Bearer ${jsonWebTokenIssuedAt(now)}',
        ],
      );
      expect(container.read(failedApiRequestTraceIdProvider), isNull);
    });

    test('API request rejected with 401 again after the retry throws the error, retries no further and reports only the retry', () async {
      final requests = recordAuthenticationRequests(
        container,
        respondFromAuth: respondWithTokenIssuedNow,
        respondFromApi: (request) async => request.uri.path == '/v1/example'
            ? ResponseBody.fromString('', 401)
            : await respondAsApi(request),
      );
      await signInWithEmail();
      now = signInTime.add(const Duration(minutes: 1));

      await expectLater(
        requestExample(),
        throwsA(
          isA<DioException>().having(
            (error) => error.response?.statusCode,
            'status code',
            401,
          ),
        ),
      );

      expect(tokenRequests(requests.auth), hasLength(2));
      expect(exampleRequests(requests.api), hasLength(2));
      expect(
        container.read(failedApiRequestTraceIdProvider),
        sentTraceId(exampleRequests(requests.api).last),
      );
    });

    test('API request whose renewal auth rejects with 401 ends the session and is canceled without reporting a failure', () async {
      var tokenStatusCode = 200;
      final requests = recordAuthenticationRequests(
        container,
        respondFromAuth: (request) async =>
            request.uri.path == tokenPath && tokenStatusCode != 200
            ? ResponseBody.fromString('', tokenStatusCode)
            : await respondWithTokenIssuedNow(request),
      );
      await signInWithEmail();
      tokenStatusCode = 401;
      now = signInTime.add(const Duration(minutes: 14));

      await expectLater(
        requestExample(),
        throwsA(
          isA<DioException>().having(
            (error) => error.type,
            'type',
            DioExceptionType.cancel,
          ),
        ),
      );

      expect(container.read(signedInAccountProvider), isNull);
      expect(exampleRequests(requests.api), isEmpty);
      expect(container.read(failedApiRequestTraceIdProvider), isNull);
    });

    for (final (scenario, failRenewal) in [
      (
        'auth failing with 500',
        (RequestOptions request) async => ResponseBody.fromString('', 500),
      ),
      (
        'auth being unreachable',
        (RequestOptions request) async => throw DioException.connectionError(
          requestOptions: request,
          reason: 'Connection refused',
        ),
      ),
    ]) {
      test(
        'API request whose renewal fails with $scenario stays signed in, throws, reports the failure and logs a warning without tokens',
        () async {
          var renewalFails = false;
          final requests = recordAuthenticationRequests(
            container,
            respondFromAuth: (request) async =>
                renewalFails && request.uri.path == tokenPath
                ? await failRenewal(request)
                : await respondWithTokenIssuedNow(request),
          );
          await signInWithEmail();
          renewalFails = true;
          now = signInTime.add(const Duration(minutes: 14));

          await expectLater(requestExample(), throwsA(isA<DioException>()));

          expect(container.read(signedInAccountProvider)?.id, accountId);
          expect(exampleRequests(requests.api), isEmpty);
          expect(container.read(failedApiRequestTraceIdProvider), isNotNull);
          final history = container.read(talkerProvider).history;
          expect(
            history.where((log) => log.logLevel == LogLevel.warning),
            hasLength(1),
          );
          expect(
            history.map((log) => log.message),
            everyElement(
              isNot(
                anyOf(
                  contains(signedSessionToken),
                  contains(jsonWebTokenIssuedAt(signInTime)),
                ),
              ),
            ),
          );
        },
      );
    }

    test('signInWithEmail stores the session token with the account id and restore after a restart signs in without waiting for the session check', () async {
      recordAuthenticationRequests(container);
      await signInWithEmail();

      expect(secureStorage, storedSession(accountId));

      final restarted = createDevelopmentContainer(await reopenPreferences());
      final pendingSessionCheck = Completer<ResponseBody>();
      final requests = recordAuthenticationRequests(
        restarted,
        respondFromAuth: (_) => pendingSessionCheck.future,
      );
      await restarted.read(signedInAccountProvider.notifier).restore();

      expect(restarted.read(signedInAccountProvider)?.id, accountId);
      await pumpEventQueue();
      expect(requests.api.requests, isEmpty);
      final sessionCheck = requests.auth.requests.single;
      expect(sessionCheck.uri.path, tokenPath);
      expect(
        sessionCheck.headers['Authorization'],
        'Bearer $signedSessionToken',
      );
      pendingSessionCheck.complete(
        jsonResponse({'token': jsonWebTokenIssuedAt(now)}),
      );
      await pumpEventQueue();
    });

    test(
      'restore without a stored session stays signed out without requests',
      () async {
        final requests = recordAuthenticationRequests(container);

        await restore();
        await pumpEventQueue();

        expect(container.read(signedInAccountProvider), isNull);
        expect(requests.auth.requests, isEmpty);
      },
    );

    for (final unreadableSession in [
      'not-json',
      '{"sessionToken":"$signedSessionToken"}',
    ]) {
      test(
        'restore with the unreadable stored session $unreadableSession stays signed out, deletes it and logs a warning without it',
        () async {
          secureStorage['session'] = unreadableSession;
          final requests = recordAuthenticationRequests(container);

          await restore();
          await pumpEventQueue();

          expect(container.read(signedInAccountProvider), isNull);
          expect(secureStorage, isEmpty);
          expect(requests.auth.requests, isEmpty);
          final log = container.read(talkerProvider).history.single;
          expect(log.logLevel, LogLevel.warning);
          expect(
            log.message,
            isNot(
              anyOf(contains(unreadableSession), contains('session-token')),
            ),
          );
        },
      );
    }

    test('restore without the installation marker clears the secure storage, sets the marker and stays signed out', () async {
      final preferences = await createPreferences();
      container = createDevelopmentContainer(preferences);
      secureStorage.addAll({...storedSession(accountId), 'other': 'value'});

      await restore();

      expect(container.read(signedInAccountProvider), isNull);
      expect(secureStorage, isEmpty);
      expect(preferences.getBool(PreferenceKeys.installed), isTrue);
    });

    test('restore whose session check auth rejects with 401 signs out and deletes the stored session', () async {
      secureStorage.addAll(storedSession(accountId));
      recordAuthenticationRequests(
        container,
        respondFromAuth: (_) async => ResponseBody.fromString('', 401),
      );

      await restore();
      await pumpEventQueue();

      expect(container.read(signedInAccountProvider), isNull);
      expect(secureStorage, isEmpty);
    });

    test('restore whose session check auth fails with 500 stays signed in and keeps the stored session', () async {
      secureStorage.addAll(storedSession(accountId));
      recordAuthenticationRequests(
        container,
        respondFromAuth: (_) async => ResponseBody.fromString('', 500),
      );

      await restore();
      await pumpEventQueue();

      expect(container.read(signedInAccountProvider)?.id, accountId);
      expect(secureStorage, storedSession(accountId));
    });
  });
}
