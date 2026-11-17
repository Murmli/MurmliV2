import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/flavor/flavor_provider.dart';

void main() {
  group('flavor', () {
    test('build without flavor argument returns development', () {
      final container = ProviderContainer.test();

      expect(container.read(flavorProvider), Flavor.development);
    });
  });
}
