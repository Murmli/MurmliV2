import 'dart:convert';

import 'package:crypto/crypto.dart';
import 'package:dio/dio.dart';
import 'package:flutter/services.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import '../api/api_client_provider.dart';
import '../logging/talker_provider.dart';
import 'account.dart';
import 'auth_client_provider.dart';

part 'signed_in_account_provider.g.dart';

enum SignInResult {
  signedIn,
  canceled,
  failed,
  // apiClientProvider already showed the error dialog, callers show nothing.
  apiRequestFailed,
}

@Riverpod(keepAlive: true)
class SignedInAccount extends _$SignedInAccount {
  @override
  Account? build() => null;

  Future<SignInResult> signInWithApple() async {
    final rawNonce = generateNonce();
    final String? identityToken;
    try {
      final credential = await SignInWithApple.getAppleIDCredential(
        scopes: [AppleIDAuthorizationScopes.email],
        nonce: sha256.convert(utf8.encode(rawNonce)).toString(),
      );
      identityToken = credential.identityToken;
    } on Exception catch (error) {
      if (error case SignInWithAppleAuthorizationException(
        code: AuthorizationErrorCode.canceled,
      )) {
        return SignInResult.canceled;
      }
      _logFailure('Apple credential', _appleErrorCode(error));
      return SignInResult.failed;
    }
    if (identityToken == null) {
      _logFailure('Apple credential', 'no identity token');
      return SignInResult.failed;
    }
    final sessionToken = await _signInWithIdToken(
      provider: 'apple',
      idToken: {'token': identityToken, 'nonce': rawNonce},
    );
    return sessionToken == null
        ? SignInResult.failed
        : _completeSignIn(sessionToken);
  }

  Future<String?> _signInWithIdToken({
    required String provider,
    required Map<String, String> idToken,
  }) async {
    const step = 'social sign-in';
    try {
      final response = await ref
          .read(authClientProvider)
          .post<void>(
            '/api/auth/sign-in/social',
            data: {'provider': provider, 'idToken': idToken},
          );
      // Only this header carries the signed session token, the one in the
      // body is unsigned.
      final sessionToken = response.headers.value('set-auth-token');
      if (sessionToken == null) {
        _logFailure(step, 'no session token');
      }
      return sessionToken;
    } on DioException catch (error) {
      _logFailure(step, _httpOutcome(error));
      return null;
    }
  }

  Future<SignInResult> _completeSignIn(String sessionToken) async {
    final jsonWebToken = await _fetchJsonWebToken(sessionToken);
    if (jsonWebToken == null) {
      return SignInResult.failed;
    }
    try {
      final response = await ref
          .read(apiClientProvider)
          .get<Map<String, Object?>>(
            '/v1/users/me',
            options: _authorizedBy(jsonWebToken),
          );
      if (response.data case {'id': final String id}) {
        state = Account(id: id);
        return SignInResult.signedIn;
      }
      _logFailure('account request', 'no account id');
      return SignInResult.failed;
    } on DioException {
      return SignInResult.apiRequestFailed;
    }
  }

  Future<String?> _fetchJsonWebToken(String sessionToken) async {
    const step = 'token request';
    try {
      final response = await ref
          .read(authClientProvider)
          .get<Map<String, Object?>>(
            '/api/auth/token',
            options: _authorizedBy(sessionToken),
          );
      if (response.data case {'token': final String jsonWebToken}) {
        return jsonWebToken;
      }
      _logFailure(step, 'no token');
      return null;
    } on DioException catch (error) {
      _logFailure(step, _httpOutcome(error));
      return null;
    }
  }

  Options _authorizedBy(String token) =>
      Options(headers: {'Authorization': 'Bearer $token'});

  void _logFailure(String step, String reason) =>
      ref.read(talkerProvider).warning('Sign-in failed at $step: $reason');
}

String _appleErrorCode(Exception error) => switch (error) {
  SignInWithAppleAuthorizationException(:final code) => code.name,
  PlatformException(:final code) => code,
  _ => error.runtimeType.toString(),
};

String _httpOutcome(DioException error) =>
    error.response?.statusCode?.toString() ?? error.type.name;
