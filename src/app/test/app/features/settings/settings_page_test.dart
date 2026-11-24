import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';
import 'package:package_info_plus/package_info_plus.dart';

import 'package:app/app/core/authentication/account.dart';
import 'package:app/app/core/authentication/signed_in_account_provider.dart';
import 'package:app/app/core/crash_reporting/crash_reporter_provider.dart';
import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/features/onboarding/onboarding_page.dart';
import 'package:app/app/features/settings/settings_page.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/crash_reporter.dart';
import '../../../helpers/preferences.dart';
import '../../../helpers/pump_app.dart';
import '../../../helpers/secure_storage.dart';

void main() {
  final germanTranslations = AppLocale.de.buildSync();
  final translations = germanTranslations.settings;

  Future<ProviderContainer> openSettings(WidgetTester tester) async {
    final container = await pumpApp(
      tester,
      signedInAccount: const Account(id: 'account'),
    );
    await tester.tap(find.byTooltip(germanTranslations.home.settingsTooltip));
    await tester.pumpAndSettle();
    return container;
  }

  Future<void> tapRow(WidgetTester tester, String label) async {
    final row = find.text(label);
    await tester.scrollUntilVisible(row, 100);
    await tester.ensureVisible(row);
    await tester.pump();
    await tester.tap(row);
    await tester.pumpAndSettle();
  }

  setUp(() {
    PackageInfo.setMockInitialValues(
      appName: 'Murmli',
      packageName: 'app',
      version: '1.2.3',
      buildNumber: '45',
      buildSignature: '',
    );
  });

  tearDown(() => LocaleSettings.setLocale(AppLocale.de));

  group('SettingsPage', () {
    testWidgets('settings button on home opens the settings page', (
      tester,
    ) async {
      await openSettings(tester);

      expect(find.byType(SettingsPage), findsOneWidget);
    });

    testWidgets('selecting dark switches the app to the dark theme', (
      tester,
    ) async {
      await openSettings(tester);

      await tapRow(tester, translations.theme.dark);

      expect(
        tester.widget<MaterialApp>(find.byType(MaterialApp)).themeMode,
        ThemeMode.dark,
      );
    });

    testWidgets('selecting english shows the texts in english', (tester) async {
      await openSettings(tester);

      await tapRow(tester, translations.language.english);

      expect(find.text('Settings'), findsOneWidget);
      expect(find.text('Einstellungen'), findsNothing);
    });

    testWidgets(
      'switching crash reports off persists it and stops crash reporting',
      (tester) async {
        final container = await openSettings(tester);

        await tapRow(tester, translations.crashReporting.send);

        final storedPreferences = await reopenPreferences();
        expect(
          storedPreferences.getBool(PreferenceKeys.crashReportingEnabled),
          isFalse,
        );
        expect(
          (container.read(
            crashReporterProvider,
          ) as RecordingCrashReporter).calls,
          ['stop'],
        );
      },
    );

    testWidgets(
      'tapping sign out ends the session at auth, deletes the stored session, removes the crash reporting user and shows the onboarding page',
      (tester) async {
        final container = await openSettings(tester);
        final requests = recordAuthenticationRequests(container);

        await tapRow(tester, translations.account.signOut);

        final signOut = requests.auth.requests.single;
        expect(signOut.method, 'POST');
        expect(signOut.uri.path, signOutPath);
        expect(signOut.headers['Authorization'], 'Bearer $signedSessionToken');
        expect(await const FlutterSecureStorage().readAll(), isEmpty);
        expect(
          (container.read(
            crashReporterProvider,
          ) as RecordingCrashReporter).calls.last,
          'user none',
        );
        expect(find.byType(OnboardingPage), findsOneWidget);
      },
    );

    testWidgets(
      'tapping sign out again while the sign-out request is pending shows a progress indicator and sends no second request',
      (tester) async {
        final container = await openSettings(tester);
        final pendingSignOut = Completer<ResponseBody>();
        final requests = recordAuthenticationRequests(
          container,
          respondFromAuth: (_) => pendingSignOut.future,
        );
        final row = find.text(translations.account.signOut);
        await tester.scrollUntilVisible(row, 100);
        await tester.ensureVisible(row);
        await tester.pump();

        await tester.tap(row);
        await tester.pump();
        await tester.tap(row);
        await tester.pump(const Duration(milliseconds: 100));

        expect(requests.auth.requests, hasLength(1));
        expect(find.byType(CircularProgressIndicator), findsOneWidget);
        pendingSignOut.complete(jsonResponse({'success': true}));
        await tester.pumpAndSettle();
        expect(find.byType(OnboardingPage), findsOneWidget);
      },
    );

    for (final (scenario, respondFromAuth) in [
      (
        'auth being unreachable',
        (RequestOptions request) async => throw DioException.connectionError(
          requestOptions: request,
          reason: 'Connection refused',
        ),
      ),
      (
        'auth failing with 503',
        (RequestOptions request) async => ResponseBody.fromString('', 503),
      ),
    ]) {
      Future<ProviderContainer> tapSignOutWhileAuthFails(
        WidgetTester tester,
      ) async {
        final container = await openSettings(tester);
        recordAuthenticationRequests(
          container,
          respondFromAuth: respondFromAuth,
        );
        await tapRow(tester, translations.account.signOut);
        return container;
      }

      testWidgets(
        'tapping sign out with $scenario and canceling the dialog stays signed in on the settings page and keeps the stored session',
        (tester) async {
          final container = await tapSignOutWhileAuthFails(tester);

          expect(
            find.text(translations.account.signOutOfflineDialog.title),
            findsOneWidget,
          );
          await tester.tap(
            find.text(translations.account.signOutOfflineDialog.cancel),
          );
          await tester.pumpAndSettle();

          expect(container.read(signedInAccountProvider), isNotNull);
          expect(
            await const FlutterSecureStorage().readAll(),
            storedSession('account'),
          );
          expect(find.byType(SettingsPage), findsOneWidget);
        },
      );

      testWidgets(
        'tapping sign out with $scenario and confirming the dialog deletes the stored session and shows the onboarding page',
        (tester) async {
          await tapSignOutWhileAuthFails(tester);

          await tester.tap(
            find.text(translations.account.signOutOfflineDialog.confirm),
          );
          await tester.pumpAndSettle();

          expect(await const FlutterSecureStorage().readAll(), isEmpty);
          expect(find.byType(OnboardingPage), findsOneWidget);
        },
      );
    }

    testWidgets(
      'confirming the sign-out on the device when the device cannot remove the stored session shows the failure popup and stays signed in on the settings page',
      (tester) async {
        final container = await openSettings(tester);
        recordAuthenticationRequests(
          container,
          respondFromAuth: (_) async => ResponseBody.fromString('', 503),
        );
        FlutterSecureStorage.setMockInitialValues(
          FailingSecureStorage(
            storedSession('account'),
            failsToClear: true,
            failsToWrite: true,
          ),
        );
        await tapRow(tester, translations.account.signOut);

        await tester.tap(
          find.text(translations.account.signOutOfflineDialog.confirm),
        );
        await tester.pumpAndSettle();

        final failedDialog = translations.account.signOutFailedDialog;
        expect(find.text(failedDialog.title), findsOneWidget);
        expect(find.text(failedDialog.message), findsOneWidget);
        await tester.tap(find.text(failedDialog.confirm));
        await tester.pumpAndSettle();
        expect(find.text(failedDialog.title), findsNothing);
        expect(container.read(signedInAccountProvider), isNotNull);
        expect(find.byType(SettingsPage), findsOneWidget);
      },
    );

    testWidgets('about section shows the app version', (tester) async {
      await openSettings(tester);

      await tester.scrollUntilVisible(find.text('1.2.3 (45)'), 100);

      expect(find.text('1.2.3 (45)'), findsOneWidget);
    });
  });
}
