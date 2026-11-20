import 'dart:convert';

import 'package:crypto/crypto.dart';
import 'package:dio/dio.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:sign_in_with_apple_platform_interface/sign_in_with_apple_platform_interface.dart';
import 'package:talker/talker.dart';

import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/logging/talker_provider.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/provider_container.dart';
import '../../../helpers/sign_in_with_apple.dart';

void main() {
  late ProviderContainer container;

  Future<SignInResult> signInWithApple() =>
      container.read(signedInAccountProvider.notifier).signInWithApple();

  String sentNonce(RequestOptions socialSignIn) =>
      ((socialSignIn.data as Map<String, Object?>)['idToken']!
          as Map<String, String>)['nonce']!;

  setUp(() async {
    container = createContainer(await createPreferences());
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
  });
}
