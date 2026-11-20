import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/app/core/authentication/auth_client_provider.dart';

import 'http_client_adapter.dart';

const socialSignInPath = '/api/auth/sign-in/social';
const tokenPath = '/api/auth/token';
const signedSessionToken = 'signed-session-token';
const jsonWebToken = 'json-web-token';
const accountId = '0198c5a4-2f6e-7b1d-9c3a-4e5f6a7b8c9d';

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
      socialSignInPath => jsonResponse(
        {'redirect': false, 'token': 'unsigned-session-token'},
        headers: {
          'set-auth-token': [signedSessionToken],
        },
      ),
      tokenPath => jsonResponse({'token': jsonWebToken}),
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
