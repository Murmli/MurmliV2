import 'dart:convert';

import 'package:crypto/crypto.dart';
import 'package:dio/dio.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:google_sign_in_platform_interface/google_sign_in_platform_interface.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:sign_in_with_apple_platform_interface/sign_in_with_apple_platform_interface.dart';
import 'package:talker/talker.dart';

import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/logging/talker_provider.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/google_sign_in.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';
import '../../../helpers/sign_in_with_apple.dart';

void main() {
  late ProviderContainer container;

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

  setUp(() async {
    container = createContainer(
      await createPreferences(),
      flavor: Flavor.development,
    );
  });

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
      expect(accountRequest.headers['Authorization'], 'Bearer $jsonWebToken');
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
      expect(accountRequest.headers['Authorization'], 'Bearer $jsonWebToken');
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
      expect(accountRequest.headers['Authorization'], 'Bearer $jsonWebToken');
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
  });
}
