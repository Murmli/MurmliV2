import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';

import 'package:app/app/features/settings/app_version_provider.dart';

void main() {
  group('appVersion', () {
    test('combines version and build number', () async {
      PackageInfo.setMockInitialValues(
        appName: 'Murmli',
        packageName: 'app',
        version: '1.2.3',
        buildNumber: '45',
        buildSignature: '',
      );
      final container = ProviderContainer.test();

      expect(await container.read(appVersionProvider.future), '1.2.3 (45)');
    });
  });
}
