import 'dart:convert';

import 'package:crypto/crypto.dart';

import 'simulation_configuration.dart';

String createJsonWebToken(
  SimulationConfiguration configuration,
  String subject,
) {
  final issuedAt =
      DateTime.now().millisecondsSinceEpoch ~/ Duration.millisecondsPerSecond;
  final unsignedToken = [
    {'alg': 'HS256', 'typ': 'JWT'},
    {
      'iss': configuration.tokenIssuer,
      'aud': configuration.tokenAudience,
      'sub': subject,
      'iat': issuedAt,
      'nbf': issuedAt,
      'exp': issuedAt + Duration.secondsPerHour,
    },
  ].map((part) => _encode(utf8.encode(jsonEncode(part)))).join('.');
  final signature = Hmac(
    sha256,
    configuration.tokenSigningKey,
  ).convert(utf8.encode(unsignedToken));
  return '$unsignedToken.${_encode(signature.bytes)}';
}

String _encode(List<int> bytes) => base64Url.encode(bytes).replaceAll('=', '');
