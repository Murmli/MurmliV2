import 'dart:convert';

import 'package:flutter/foundation.dart';

@immutable
class AccessToken {
  const AccessToken._(this.value, this._dueAt);

  factory AccessToken.parse(
    String jsonWebToken, {
    required DateTime receivedAt,
  }) {
    final parts = jsonWebToken.split('.');
    if (parts.length != 3) {
      throw const FormatException('The access token is no JSON web token.');
    }
    final payload = jsonDecode(
      utf8.decode(base64Url.decode(base64Url.normalize(parts[1]))),
    );
    if (payload case {'iat': final int issuedAt, 'exp': final int expiresAt}) {
      // Counted from receipt instead of comparing exp with the device clock,
      // which may be wrong.
      final lifetime = Duration(seconds: expiresAt - issuedAt);
      return AccessToken._(
        jsonWebToken,
        receivedAt.add(lifetime - _renewalMargin),
      );
    }
    throw const FormatException('The access token carries no iat or exp.');
  }

  final String value;
  final DateTime _dueAt;

  bool isDueAt(DateTime now) => !now.isBefore(_dueAt);
}

const _renewalMargin = Duration(minutes: 1);
