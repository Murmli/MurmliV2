import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/router/app_router.dart';
import 'package:app/app/features/home/widgets/floating_navigation_bar.dart';
import 'package:app/app/features/home/home_page.dart';
import 'package:app/app/features/home/home_tab.dart';
import 'package:app/app/features/home/tabs/chat_page.dart';
import 'package:app/app/features/onboarding/onboarding_page.dart';
import 'package:app/app/features/settings/settings_page.dart';

import '../../../helpers/pump_app.dart';

void main() {
  group('AppRouter', () {
    testWidgets('start while signed out shows the onboarding page', (
      tester,
    ) async {
      await pumpApp(tester);

      expect(find.byType(OnboardingPage), findsOneWidget);
      expect(find.byType(HomePage), findsNothing);
    });

    testWidgets('start while signed in shows home with the chat tab active', (
      tester,
    ) async {
      await pumpApp(tester, signedInAccount: const Account(id: 'account'));

      expect(find.byType(OnboardingPage), findsNothing);
      expect(find.byType(ChatPage), findsOneWidget);
      expect(
        tester
            .widget<FloatingNavigationBar>(find.byType(FloatingNavigationBar))
            .activeTab,
        HomeTab.chat,
      );
    });

    testWidgets(
      'navigating to settings while signed out shows the onboarding page',
      (tester) async {
        await pumpApp(tester);

        unawaited(
          tester
              .element(find.byType(OnboardingPage))
              .router
              .push(const SettingsRoute()),
        );
        await tester.pumpAndSettle();

        expect(find.byType(SettingsPage), findsNothing);
        expect(find.byType(OnboardingPage), findsOneWidget);
      },
    );
  });
}
