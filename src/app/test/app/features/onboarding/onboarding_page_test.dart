import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:google_sign_in_platform_interface/google_sign_in_platform_interface.dart';
import 'package:material_ui/material_ui.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/features/home/home_page.dart';
import 'package:app/app/features/onboarding/onboarding_page.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/google_sign_in.dart';
import '../../../helpers/pump_app.dart';
import '../../../helpers/sign_in_with_apple.dart';

void main() {
  final germanTranslations = AppLocale.de.buildSync();
  final translations = germanTranslations.onboarding;
  final requestFailureTranslations = germanTranslations.requestFailure;

  final mobilePlatforms = TargetPlatformVariant({
    TargetPlatform.iOS,
    TargetPlatform.android,
  });

  Finder emailField() =>
      find.widgetWithText(TextField, translations.emailSignIn.email);

  Finder passwordField() =>
      find.widgetWithText(TextField, translations.emailSignIn.password);

  Finder submitButton() =>
      find.widgetWithText(FilledButton, translations.emailSignIn.submit);

  Finder googleButton() =>
      find.bySemanticsLabel(translations.continueWithGoogle);

  Future<void> enterCredentials(WidgetTester tester) async {
    await tester.enterText(emailField(), '  $signInEmail ');
    await tester.enterText(passwordField(), signInPassword);
  }

  Future<void> tapAppleButton(WidgetTester tester) async {
    final appleButton = find.byType(SignInWithAppleButton);
    await tester.ensureVisible(appleButton);
    await tester.pump();
    await tester.tap(appleButton);
    await tester.pumpAndSettle();
  }

  Future<void> tapGoogleButton(WidgetTester tester) async {
    await tester.ensureVisible(googleButton());
    await tester.pump();
    await tester.tap(googleButton());
    await tester.pumpAndSettle();
  }

  group('OnboardingPage', () {
    testWidgets(
      'with the flavor development tapping the Apple button with a successful sign-in opens home',
      variant: TargetPlatformVariant.only(TargetPlatform.iOS),
      (tester) async {
        installFakeSignInWithApple();
        final container = await pumpApp(tester, flavor: Flavor.development);
        recordAuthenticationRequests(container);

        await tapAppleButton(tester);

        expect(find.byType(HomePage), findsOneWidget);
        expect(find.byType(OnboardingPage), findsNothing);
      },
    );

    testWidgets(
      'with the flavor development tapping the Apple button with a failed sign-in shows the failure and the Apple button again',
      variant: TargetPlatformVariant.only(TargetPlatform.iOS),
      (tester) async {
        installFakeSignInWithApple();
        final container = await pumpApp(tester, flavor: Flavor.development);
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
      'with the flavor development canceling the Apple dialog shows the Apple button again without a snackbar',
      variant: TargetPlatformVariant.only(TargetPlatform.iOS),
      (tester) async {
        installFakeSignInWithApple(
          exception: const SignInWithAppleAuthorizationException(
            code: AuthorizationErrorCode.canceled,
            message: 'The user canceled the authorization attempt.',
          ),
        );
        await pumpApp(tester, flavor: Flavor.development);

        await tapAppleButton(tester);

        expect(find.byType(OnboardingPage), findsOneWidget);
        expect(find.byType(SnackBar), findsNothing);
        expect(find.byType(SignInWithAppleButton), findsOneWidget);
      },
    );

    testWidgets(
      'with the flavor development on Android tapping the Google button with a successful sign-in opens home',
      variant: TargetPlatformVariant.only(TargetPlatform.android),
      (tester) async {
        installFakeGoogleSignIn();
        final container = await pumpApp(tester, flavor: Flavor.development);
        recordAuthenticationRequests(container);

        await tapGoogleButton(tester);

        expect(find.byType(HomePage), findsOneWidget);
        expect(find.byType(OnboardingPage), findsNothing);
      },
    );

    testWidgets(
      'with the flavor development on Android canceling the Google dialog shows the Google button again without a snackbar',
      variant: TargetPlatformVariant.only(TargetPlatform.android),
      (tester) async {
        installFakeGoogleSignIn(
          exception: const GoogleSignInException(
            code: GoogleSignInExceptionCode.canceled,
          ),
        );
        await pumpApp(tester, flavor: Flavor.development);

        await tapGoogleButton(tester);

        expect(find.byType(OnboardingPage), findsOneWidget);
        expect(find.byType(SnackBar), findsNothing);
        expect(
          tester.getSemantics(googleButton()),
          isSemantics(isButton: true, isEnabled: true, hasTapAction: true),
        );
      },
    );

    testWidgets(
      'with the flavor development on Android tapping the Google button again while the sign-in is running starts no second sign-in',
      variant: TargetPlatformVariant.only(TargetPlatform.android),
      (tester) async {
        final googleSignIn = installFakeGoogleSignIn();
        final container = await pumpApp(tester, flavor: Flavor.development);
        final socialSignInResponseReleased = Completer<void>();
        recordAuthenticationRequests(
          container,
          respondFromAuth: (request) async {
            if (request.uri.path == socialSignInPath) {
              await socialSignInResponseReleased.future;
            }
            return respondAsAuth(request);
          },
        );
        await tester.ensureVisible(googleButton());
        await tester.pump();

        await tester.tap(googleButton());
        await tester.pump();
        await tester.tap(googleButton());
        await tester.pump();

        expect(
          tester.getSemantics(googleButton()),
          isSemantics(isButton: true, isEnabled: false, hasTapAction: false),
        );
        socialSignInResponseReleased.complete();
        await tester.pumpAndSettle();
        expect(googleSignIn.authenticateCalls, 1);
        expect(find.byType(HomePage), findsOneWidget);
      },
    );

    testWidgets(
      'with the flavor development on Android the Google button has a tap area at least 48 high',
      variant: TargetPlatformVariant.only(TargetPlatform.android),
      (tester) async {
        await pumpApp(tester, flavor: Flavor.development);

        expect(
          tester.getSize(googleButton()).height,
          greaterThanOrEqualTo(kMinInteractiveDimension),
        );
      },
    );

    for (final flavor in [Flavor.development, Flavor.production]) {
      testWidgets(
        'with the flavor ${flavor.name} on Android shows the Google button and no Apple button',
        variant: TargetPlatformVariant.only(TargetPlatform.android),
        (tester) async {
          await pumpApp(tester, flavor: flavor);

          expect(find.byType(OnboardingPage), findsOneWidget);
          expect(googleButton(), findsOneWidget);
          expect(find.byType(SignInWithAppleButton), findsNothing);
        },
      );

      testWidgets(
        'with the flavor ${flavor.name} on iOS shows the Apple button and no Google button',
        variant: TargetPlatformVariant.only(TargetPlatform.iOS),
        (tester) async {
          await pumpApp(tester, flavor: flavor);

          expect(find.byType(SignInWithAppleButton), findsOneWidget);
          expect(googleButton(), findsNothing);
        },
      );

      testWidgets(
        'with the flavor ${flavor.name} shows no email form',
        variant: mobilePlatforms,
        (tester) async {
          await pumpApp(tester, flavor: flavor);

          expect(find.byType(OnboardingPage), findsOneWidget);
          expect(emailField(), findsNothing);
          expect(passwordField(), findsNothing);
          expect(submitButton(), findsNothing);
        },
      );
    }

    testWidgets(
      'with the flavor local shows the email form and neither the Apple nor the Google button',
      variant: mobilePlatforms,
      (tester) async {
        await pumpApp(tester, flavor: Flavor.local);

        expect(emailField(), findsOneWidget);
        expect(passwordField(), findsOneWidget);
        expect(submitButton(), findsOneWidget);
        expect(find.byType(SignInWithAppleButton), findsNothing);
        expect(googleButton(), findsNothing);
      },
    );

    testWidgets(
      'with the flavor local tapping sign-in sends the trimmed email with the password and opens home',
      (tester) async {
        final container = await pumpApp(tester, flavor: Flavor.local);
        final requests = recordAuthenticationRequests(container);
        await enterCredentials(tester);

        await tester.ensureVisible(submitButton());
        await tester.pump();
        await tester.tap(submitButton());
        await tester.pumpAndSettle();

        expect(requests.auth.requests.first.data, {
          'email': signInEmail,
          'password': signInPassword,
        });
        expect(find.byType(HomePage), findsOneWidget);
        expect(find.byType(OnboardingPage), findsNothing);
      },
    );

    testWidgets(
      'with the flavor local submitting rejected credentials from the password field shows the invalid credentials message and the form again',
      (tester) async {
        final container = await pumpApp(tester, flavor: Flavor.local);
        recordAuthenticationRequests(
          container,
          respondFromAuth: (_) async => ResponseBody.fromString('', 401),
        );
        await enterCredentials(tester);

        await tester.testTextInput.receiveAction(TextInputAction.done);
        await tester.pumpAndSettle();

        expect(find.byType(OnboardingPage), findsOneWidget);
        expect(
          find.widgetWithText(
            SnackBar,
            translations.emailSignIn.invalidCredentials,
          ),
          findsOneWidget,
        );
        expect(emailField(), findsOneWidget);
        expect(submitButton(), findsOneWidget);
      },
    );

    for (final (scenario, respondFromAuth, expectedToast) in [
      (
        'auth being unreachable',
        (RequestOptions request) async => throw DioException.connectionError(
          requestOptions: request,
          reason: 'Connection refused',
        ),
        requestFailureTranslations.noConnection,
      ),
      (
        'auth answering with 429',
        (RequestOptions request) async => ResponseBody.fromString('', 429),
        requestFailureTranslations.tooManyRequests,
      ),
    ]) {
      testWidgets(
        'with the flavor local submitting the email sign-in with $scenario shows its toast instead of the sign-in failure and the form again',
        (tester) async {
          final container = await pumpApp(tester, flavor: Flavor.local);
          recordAuthenticationRequests(
            container,
            respondFromAuth: respondFromAuth,
          );
          await enterCredentials(tester);

          await tester.testTextInput.receiveAction(TextInputAction.done);
          await tester.pumpAndSettle();

          expect(find.widgetWithText(SnackBar, expectedToast), findsOneWidget);
          expect(find.text(translations.signInFailed), findsNothing);
          expect(submitButton(), findsOneWidget);
        },
      );
    }

    testWidgets(
      'with the flavor local submitting again while the email sign-in is running disables the fields and sends no second request',
      (tester) async {
        final container = await pumpApp(tester, flavor: Flavor.local);
        final emailSignInResponseReleased = Completer<void>();
        final requests = recordAuthenticationRequests(
          container,
          respondFromAuth: (request) async {
            if (request.uri.path == emailSignInPath) {
              await emailSignInResponseReleased.future;
            }
            return respondAsAuth(request);
          },
        );
        await enterCredentials(tester);
        await tester.ensureVisible(submitButton());
        await tester.pump();

        await tester.tap(submitButton());
        await tester.testTextInput.receiveAction(TextInputAction.done);
        await tester.tap(find.byType(FilledButton));
        await tester.pump();

        expect(tester.widget<TextField>(emailField()).enabled, isFalse);
        expect(tester.widget<TextField>(passwordField()).enabled, isFalse);
        expect(
          tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
          isNull,
        );
        emailSignInResponseReleased.complete();
        await tester.pumpAndSettle();
        expect(
          requests.auth.requests.where(
            (request) => request.uri.path == emailSignInPath,
          ),
          hasLength(1),
        );
        expect(find.byType(HomePage), findsOneWidget);
      },
    );
  });
}
