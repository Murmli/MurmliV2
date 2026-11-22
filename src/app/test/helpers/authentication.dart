import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/authentication/auth_client_provider.dart';
import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/logging/talker_provider.dart';

import 'http_client_adapter.dart';

const socialSignInPath = '/api/auth/sign-in/social';
const emailSignInPath = '/api/auth/sign-in/email';
const tokenPath = '/api/auth/token';
const signInEmail = 'test@murmli.de';
const signInPassword = 'correct-horse-battery-staple';
const signedSessionToken = 'signed-session-token';
const accountId = '0198c5a4-2f6e-7b1d-9c3a-4e5f6a7b8c9d';
final issuedJsonWebToken = jsonWebTokenIssuedAt(DateTime.utc(2026, 11, 22));

String jsonWebTokenIssuedAt(
  DateTime issuedAt, {
  Duration lifetime = const Duration(minutes: 15),
}) {
  String encode(Map<String, Object> json) =>
      base64Url.encode(utf8.encode(jsonEncode(json))).replaceAll('=', '');
  final issuedAtSeconds = issuedAt.millisecondsSinceEpoch ~/ 1000;
  return [
    encode({'alg': 'ES256', 'typ': 'JWT'}),
    encode({
      'iat': issuedAtSeconds,
      'exp': issuedAtSeconds + lifetime.inSeconds,
    }),
    'signature',
  ].join('.');
}

ResponseBody jsonResponse(
  Object body, {
  int statusCode = 200,
  Map<String, List<String>> headers = const {},
}) => ResponseBody.fromString(
  jsonEncode(body),
  statusCode,
  headers: {
    Headers.contentTypeHeader: [Headers.jsonContentType],
    ...headers,
  },
);

Future<ResponseBody> respondAsAuth(RequestOptions request) async =>
    switch (request.uri.path) {
      socialSignInPath || emailSignInPath => jsonResponse(
        {'redirect': false, 'token': 'unsigned-session-token'},
        headers: {
          'set-auth-token': [signedSessionToken],
        },
      ),
      tokenPath => jsonResponse({'token': issuedJsonWebToken}),
      _ => ResponseBody.fromString('', 404),
    };

Future<ResponseBody> respondAsApi(RequestOptions request) async =>
    jsonResponse({'id': accountId});

({RecordingHttpClientAdapter auth, RecordingHttpClientAdapter api})
recordAuthenticationRequests(
  ProviderContainer container, {
  Future<ResponseBody> Function(RequestOptions request) respondFromAuth =
      respondAsAuth,
  Future<ResponseBody> Function(RequestOptions request) respondFromApi =
      respondAsApi,
}) {
  final auth = RecordingHttpClientAdapter(respond: respondFromAuth);
  final api = RecordingHttpClientAdapter(respond: respondFromApi);
  container.read(authClientProvider).httpClientAdapter = auth;
  container.read(apiClientProvider).httpClientAdapter = api;
  return (auth: auth, api: api);
}

Future<void> signIn(ProviderContainer container) async {
  recordAuthenticationRequests(container);
  await container
      .read(signedInAccountProvider.notifier)
      .signInWithEmail(email: signInEmail, password: signInPassword);
  container.read(talkerProvider).cleanHistory();
}
