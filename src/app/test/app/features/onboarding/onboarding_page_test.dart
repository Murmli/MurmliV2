import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:material_ui/material_ui.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import 'package:app/app/features/home/home_page.dart';
import 'package:app/app/features/onboarding/onboarding_page.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/pump_app.dart';
import '../../../helpers/sign_in_with_apple.dart';

void main() {
  final translations = AppLocale.de.buildSync().onboarding;

  Future<void> tapAppleButton(WidgetTester tester) async {
    final appleButton = find.byType(SignInWithAppleButton);
    await tester.ensureVisible(appleButton);
    await tester.pump();
    await tester.tap(appleButton);
    await tester.pumpAndSettle();
  }

  group('OnboardingPage', () {
    testWidgets(
      'tapping the Apple button with a successful sign-in opens home',
      variant: TargetPlatformVariant.only(TargetPlatform.iOS),
      (tester) async {
        installFakeSignInWithApple();
        final container = await pumpApp(tester);
        recordAuthenticationRequests(container);

        await tapAppleButton(tester);

        expect(find.byType(HomePage), findsOneWidget);
        expect(find.byType(OnboardingPage), findsNothing);
      },
    );

    testWidgets(
      'tapping the Apple button with a failed sign-in shows the failure and the Apple button again',
      variant: TargetPlatformVariant.only(TargetPlatform.iOS),
      (tester) async {
        installFakeSignInWithApple();
        final container = await pumpApp(tester);
        recordAuthenticationRequests(
          container,
          respondFromAuth: (_) async => ResponseBody.fromString('', 401),
        );

        await tapAppleButton(tester);

        expect(find.byType(OnboardingPage), findsOneWidget);
        expect(
          find.widgetWithText(SnackBar, translations.signInFailed),
          findsOneWidget,
        );
        expect(find.byType(SignInWithAppleButton), findsOneWidget);
      },
    );

    testWidgets(
      'canceling the Apple dialog shows the Apple button again without a snackbar',
      variant: TargetPlatformVariant.only(TargetPlatform.iOS),
      (tester) async {
        installFakeSignInWithApple(
          exception: const SignInWithAppleAuthorizationException(
            code: AuthorizationErrorCode.canceled,
            message: 'The user canceled the authorization attempt.',
          ),
        );
        await pumpApp(tester);

        await tapAppleButton(tester);

        expect(find.byType(OnboardingPage), findsOneWidget);
        expect(find.byType(SnackBar), findsNothing);
        expect(find.byType(SignInWithAppleButton), findsOneWidget);
      },
    );

    testWidgets(
      'on Android shows no Apple button',
      variant: TargetPlatformVariant.only(TargetPlatform.android),
      (tester) async {
        await pumpApp(tester);

        expect(find.byType(OnboardingPage), findsOneWidget);
        expect(find.byType(SignInWithAppleButton), findsNothing);
      },
    );
  });
}
