import 'dart:async';
import 'dart:convert';

import 'package:crypto/crypto.dart';
import 'package:dio/dio.dart';
import 'package:flutter/services.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:google_sign_in_platform_interface/google_sign_in_platform_interface.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import '../api/api_client_provider.dart';
import '../flavor/flavor.dart';
import '../flavor/flavor_provider.dart';
import '../logging/talker_provider.dart';
import '../storage/preference_keys.dart';
import '../storage/shared_preferences_provider.dart';
import '../time/clock_provider.dart';
import 'access_token.dart';
import 'account.dart';
import 'auth_client_provider.dart';

part 'signed_in_account_provider.g.dart';

const _secureStorage = FlutterSecureStorage(
  iOptions: IOSOptions(
    accessibility: KeychainAccessibility.first_unlock_this_device,
  ),
);
const _sessionKey = 'session';

enum SignInResult {
  signedIn,
  canceled,
  failed,
  invalidCredentials,
  // apiClientProvider already showed the error dialog, callers show nothing.
  apiRequestFailed,
}

enum SignOutResult { signedOut, authUnreachable }

@Riverpod(keepAlive: true)
class SignedInAccount extends _$SignedInAccount {
  late final Future<void> _googleSignInInitialization = GoogleSignInPlatform
      .instance
      .init(
        InitParameters(
          serverClientId: _googleServerClientIdOf(ref.read(flavorProvider)),
        ),
      );

  String? _sessionToken;
  AccessToken? _accessToken;
  final _accessTokensOfSession = <String>{};
  Future<AccessToken?>? _renewal;

  @override
  Account? build() => null;

  Future<void> restore() async {
    final String? storedSession;
    try {
      await _clearSecureStorageAfterReinstall();
      storedSession = await _secureStorage.read(key: _sessionKey);
    } on PlatformException catch (error) {
      _logStorageFailure('restore', error);
      return;
    }
    if (storedSession == null) {
      return;
    }
    final session = _sessionOf(storedSession);
    if (session == null) {
      ref
          .read(talkerProvider)
          .warning('Stored session is unreadable, staying signed out');
      await _deleteStoredSession();
      return;
    }
    _sessionToken = session.sessionToken;
    state = Account(id: session.accountId);
    unawaited(_checkRestoredSession());
  }

  Future<void> _clearSecureStorageAfterReinstall() async {
    final preferences = ref.read(sharedPreferencesProvider);
    if (preferences.getBool(PreferenceKeys.installed) != null) {
      return;
    }
    // The iOS Keychain keeps its entries when the app is uninstalled.
    await _secureStorage.deleteAll();
    await preferences.setBool(PreferenceKeys.installed, true);
  }

  Future<void> _checkRestoredSession() async {
    try {
      await accessToken();
    } on DioException {
      // accessToken logged the failure, the session stays until auth rejects it.
    }
  }

  Future<SignInResult> signInWithApple() async {
    final rawNonce = generateNonce();
    final String? identityToken;
    try {
      final credential = await SignInWithApple.getAppleIDCredential(
        scopes: [],
        nonce: sha256.convert(utf8.encode(rawNonce)).toString(),
      );
      identityToken = credential.identityToken;
    } on Exception catch (error) {
      if (error case SignInWithAppleAuthorizationException(
        code: AuthorizationErrorCode.canceled,
      )) {
        return SignInResult.canceled;
      }
      _logFailure('Apple credential', _credentialErrorCode(error));
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

  Future<SignInResult> signInWithGoogle() async {
    final String? idToken;
    try {
      await _googleSignInInitialization;
      final results = await GoogleSignInPlatform.instance.authenticate(
        const AuthenticateParameters(),
      );
      idToken = results.authenticationTokens.idToken;
    } on Exception catch (error) {
      if (error case GoogleSignInException(
        code: GoogleSignInExceptionCode.canceled,
      )) {
        return SignInResult.canceled;
      }
      _logFailure('Google credential', _credentialErrorCode(error));
      return SignInResult.failed;
    }
    if (idToken == null) {
      _logFailure('Google credential', 'no ID token');
      return SignInResult.failed;
    }
    final sessionToken = await _signInWithIdToken(
      provider: 'google',
      idToken: {'token': idToken},
    );
    return sessionToken == null
        ? SignInResult.failed
        : _completeSignIn(sessionToken);
  }

  Future<SignInResult> signInWithEmail({
    required String email,
    required String password,
  }) async {
    const step = 'email sign-in';
    final String? sessionToken;
    try {
      final response = await ref
          .read(authClientProvider)
          .post<void>(
            '/api/auth/sign-in/email',
            data: {'email': email, 'password': password},
          );
      sessionToken = _sessionTokenOf(response, step);
    } on DioException catch (error) {
      _logFailure(step, _httpOutcome(error));
      return switch (error.response?.statusCode) {
        // Better Auth answers a malformed address with 400, it gets the same
        // message as wrong credentials.
        401 || 400 => SignInResult.invalidCredentials,
        _ => SignInResult.failed,
      };
    }
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
      return _sessionTokenOf(response, step);
    } on DioException catch (error) {
      _logFailure(step, _httpOutcome(error));
      return null;
    }
  }

  String? _sessionTokenOf(Response<void> response, String step) {
    // Only this header carries the signed session token, the one in the body
    // is unsigned.
    final sessionToken = response.headers.value('set-auth-token');
    if (sessionToken == null) {
      _logFailure(step, 'no session token');
    }
    return sessionToken;
  }

  Future<SignOutResult> signOut() async {
    final sessionToken = _sessionToken;
    if (sessionToken != null) {
      try {
        await ref
            .read(authClientProvider)
            .post<void>(
              '/api/auth/sign-out',
              options: _authorizedBy(sessionToken),
            );
      } on DioException catch (error) {
        ref
            .read(talkerProvider)
            .warning('Sign-out failed: ${_httpOutcome(error)}');
        return SignOutResult.authUnreachable;
      }
    }
    await _endSession();
    return SignOutResult.signedOut;
  }

  Future<void> signOutLocally() => _endSession();

  /// A [rejected] token that the current session did not issue yields null,
  /// so a request sent under an earlier session is never resent under this
  /// one.
  Future<String?> accessToken({String? rejected}) async {
    try {
      return await _validAccessToken(rejected: rejected);
    } on DioException catch (error) {
      ref
          .read(talkerProvider)
          .warning('Access token renewal failed: ${_renewalOutcome(error)}');
      rethrow;
    }
  }

  Future<String?> _validAccessToken({String? rejected}) async {
    final sessionToken = _sessionToken;
    if (sessionToken == null ||
        (rejected != null && !_accessTokensOfSession.contains(rejected))) {
      return null;
    }
    final cachedAccessToken = _accessToken;
    if (cachedAccessToken != null &&
        !cachedAccessToken.isDueAt(ref.read(appClockProvider).now()) &&
        cachedAccessToken.value != rejected) {
      return cachedAccessToken.value;
    }
    return (await _sharedRenewal(sessionToken))?.value;
  }

  Future<AccessToken?> _sharedRenewal(String sessionToken) {
    final runningRenewal = _renewal;
    if (runningRenewal != null) {
      return runningRenewal;
    }
    late final Future<AccessToken?> renewal;
    renewal = _renew(sessionToken).whenComplete(() {
      if (identical(_renewal, renewal)) {
        _renewal = null;
      }
    });
    return _renewal = renewal;
  }

  Future<AccessToken?> _renew(String sessionToken) async {
    final Response<Map<String, Object?>> response;
    try {
      response = await ref
          .read(authClientProvider)
          .get<Map<String, Object?>>(
            '/api/auth/token',
            options: _authorizedBy(sessionToken),
          );
    } on DioException catch (error) {
      if (error.response?.statusCode == 401) {
        if (_sessionToken == sessionToken) {
          await _endSession();
        }
        return null;
      }
      rethrow;
    }
    final AccessToken accessToken;
    try {
      accessToken = switch (response.data) {
        {'token': final String jsonWebToken} => AccessToken.parse(
          jsonWebToken,
          receivedAt: ref.read(appClockProvider).now(),
        ),
        _ => throw const FormatException('The response carries no token.'),
      };
    } on FormatException catch (error) {
      throw DioException(requestOptions: response.requestOptions, error: error);
    }
    if (_sessionToken != sessionToken) {
      return null;
    }
    _accessToken = accessToken;
    _accessTokensOfSession.add(accessToken.value);
    return accessToken;
  }

  Future<void> _endSession() async {
    _startSession(null);
    state = null;
    await _deleteStoredSession();
  }

  void _startSession(String? sessionToken) {
    _sessionToken = sessionToken;
    _accessToken = null;
    _accessTokensOfSession.clear();
    _renewal = null;
  }

  Future<void> _deleteStoredSession() async {
    try {
      await _secureStorage.delete(key: _sessionKey);
    } on PlatformException {
      try {
        await _secureStorage.deleteAll();
      } on PlatformException catch (error) {
        _logStorageFailure('delete', error);
      }
    }
  }

  Future<SignInResult> _completeSignIn(String sessionToken) async {
    _startSession(sessionToken);
    const step = 'token request';
    try {
      if (await _validAccessToken() == null) {
        _logFailure(step, 'session rejected');
        return SignInResult.failed;
      }
    } on DioException catch (error) {
      await _endSession();
      _logFailure(step, _renewalOutcome(error));
      return SignInResult.failed;
    }
    try {
      final response = await ref
          .read(apiClientProvider)
          .get<Map<String, Object?>>('/v1/users/me');
      if (response.data case {'id': final String id}) {
        return await _storeSession(sessionToken: sessionToken, accountId: id);
      }
      await _endSession();
      _logFailure('account request', 'no account id');
      return SignInResult.failed;
    } on DioException {
      await _endSession();
      return SignInResult.apiRequestFailed;
    }
  }

  Future<SignInResult> _storeSession({
    required String sessionToken,
    required String accountId,
  }) async {
    try {
      await _secureStorage.write(
        key: _sessionKey,
        value: jsonEncode({
          'sessionToken': sessionToken,
          'accountId': accountId,
        }),
      );
    } on PlatformException catch (error) {
      await _endSession();
      _logFailure('session storage', error.code);
      return SignInResult.failed;
    }
    state = Account(id: accountId);
    return SignInResult.signedIn;
  }

  Options _authorizedBy(String token) =>
      Options(headers: {'Authorization': 'Bearer $token'});

  void _logFailure(String step, String reason) =>
      ref.read(talkerProvider).warning('Sign-in failed at $step: $reason');

  void _logStorageFailure(String operation, PlatformException error) => ref
      .read(talkerProvider)
      .warning('Secure storage $operation failed: ${error.code}');
}

({String sessionToken, String accountId})? _sessionOf(String storedSession) {
  try {
    return switch (jsonDecode(storedSession)) {
      {
        'sessionToken': final String sessionToken,
        'accountId': final String accountId,
      } =>
        (sessionToken: sessionToken, accountId: accountId),
      _ => null,
    };
  } on FormatException {
    return null;
  }
}

// local shares the app ID de.murmli.app.dev and with it the Google client.
String _googleServerClientIdOf(Flavor flavor) => switch (flavor) {
  Flavor.local || Flavor.development =>
    '919671347923-6osa4hn7cc3f8trnbdb8s826i0jfv63b.apps.googleusercontent.com',
  Flavor.production => throw UnimplementedError(
    'The production flavor has no Google client until the release.',
  ),
};

String _credentialErrorCode(Exception error) => switch (error) {
  SignInWithAppleAuthorizationException(:final code) => code.name,
  GoogleSignInException(:final code) => code.name,
  PlatformException(:final code) => code,
  _ => error.runtimeType.toString(),
};

String _httpOutcome(DioException error) =>
    error.response?.statusCode?.toString() ?? error.type.name;

String _renewalOutcome(DioException error) =>
    error.error is FormatException ? 'unreadable token' : _httpOutcome(error);
