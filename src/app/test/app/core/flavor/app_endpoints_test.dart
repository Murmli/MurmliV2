import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/flavor/app_endpoints.dart';
import 'package:app/app/core/flavor/flavor.dart';

void main() {
  group('AppEndpoints', () {
    test('of local on android points to the host via 10.0.2.2', () {
      final endpoints = AppEndpoints.of(Flavor.local, TargetPlatform.android);

      expect(endpoints.api, Uri.parse('http://10.0.2.2:5020'));
      expect(endpoints.auth, Uri.parse('http://10.0.2.2:5021'));
    });

    test('of local on iOS points to localhost', () {
      final endpoints = AppEndpoints.of(Flavor.local, TargetPlatform.iOS);

      expect(endpoints.api, Uri.parse('http://localhost:5020'));
      expect(endpoints.auth, Uri.parse('http://localhost:5021'));
    });
  });
}
