import 'package:flutter_test/flutter_test.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/widgets/flavor_banner.dart';

import '../../../helpers/pump_app.dart';

void main() {
  final flavorBanner = find.descendant(
    of: find.byType(FlavorBanner),
    matching: find.byType(Banner),
  );

  group('FlavorBanner', () {
    testWidgets('local flavor shows a banner labeled local', (tester) async {
      await pumpApp(tester, flavor: Flavor.local);

      expect(tester.widget<Banner>(flavorBanner).message, 'local');
    });

    testWidgets('development flavor shows a banner labeled dev', (
      tester,
    ) async {
      await pumpApp(tester, flavor: Flavor.development);

      expect(tester.widget<Banner>(flavorBanner).message, 'dev');
    });

    testWidgets('production flavor shows no banner', (tester) async {
      await pumpApp(tester, flavor: Flavor.production);

      expect(flavorBanner, findsNothing);
    });
  });
}
