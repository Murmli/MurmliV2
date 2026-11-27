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
import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/storage/preference_keys.dart';
import 'package:app/app/core/widgets/slide_to_confirm.dart';
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

  Future<void> slideToDelete(WidgetTester tester) async {
    final slider = find.byType(SlideToConfirm);
    await tester.drag(slider, Offset(tester.getSize(slider).width, 0));
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

    group('deleting the account', () {
      late final Translations englishTranslations;
      final storedPreferencesBeforeDeletion = {
        PreferenceKeys.appLanguage: 'english',
        PreferenceKeys.themeMode: 'dark',
      };

      setUpAll(() async => englishTranslations = await AppLocale.en.build());

      Future<ProviderContainer> openSettingsWithStoredPreferences(
        WidgetTester tester, {
        required bool isCrashReportingEnabled,
      }) async {
        final container = await pumpApp(
          tester,
          storedPreferences: {
            PreferenceKeys.crashReportingEnabled: isCrashReportingEnabled,
            ...storedPreferencesBeforeDeletion,
          },
          signedInAccount: const Account(id: 'account'),
        );
        await tester.tap(
          find.byTooltip(englishTranslations.home.settingsTooltip),
        );
        await tester.pumpAndSettle();
        return container;
      }

      Future<void> confirmDeletion(WidgetTester tester) async {
        final account = englishTranslations.settings.account;
        await tapRow(tester, account.deleteAccount);
        await slideToDelete(tester);
        await tester.pumpAndSettle();
      }

      Future<void> expectDeviceDataRemovedOnOnboarding(
        WidgetTester tester, {
        required bool isCrashReportingEnabled,
      }) async {
        expect(await const FlutterSecureStorage().readAll(), isEmpty);
        final storedPreferences = await reopenPreferences();
        expect(storedPreferences.keys, {
          PreferenceKeys.crashReportingEnabled,
          PreferenceKeys.installed,
        });
        expect(
          storedPreferences.getBool(PreferenceKeys.crashReportingEnabled),
          isCrashReportingEnabled,
        );
        expect(find.byType(OnboardingPage), findsOneWidget);
        expect(
          tester.widget<MaterialApp>(find.byType(MaterialApp)).themeMode,
          ThemeMode.system,
        );
        expect(LocaleSettings.currentLocale, AppLocale.de);
      }

      testWidgets(
        'tapping delete account and canceling the dialog sends no request and stays signed in on the settings page',
        (tester) async {
          final container = await openSettings(tester);
          final requests = recordAuthenticationRequests(container);

          await tapRow(tester, translations.account.deleteAccount);

          expect(
            find.text(translations.account.deleteDialog.message),
            findsOneWidget,
          );
          await tester.tap(find.text(translations.account.deleteDialog.cancel));
          await tester.pumpAndSettle();

          expect(requests.api.requests, isEmpty);
          expect(container.read(signedInAccountProvider), isNotNull);
          expect(find.byType(SettingsPage), findsOneWidget);
        },
      );

      testWidgets(
        'sliding only partway and releasing sends no request, keeps the dialog open and stays signed in',
        (tester) async {
          final container = await openSettings(tester);
          final requests = recordAuthenticationRequests(container);
          await tapRow(tester, translations.account.deleteAccount);

          final slider = find.byType(SlideToConfirm);
          await tester.drag(
            slider,
            Offset(tester.getSize(slider).width / 3, 0),
          );
          await tester.pumpAndSettle();

          expect(requests.api.requests, isEmpty);
          expect(
            find.text(translations.account.deleteDialog.message),
            findsOneWidget,
          );
          expect(container.read(signedInAccountProvider), isNotNull);
        },
      );

      testWidgets(
        'confirming the deletion deletes the account at the API, removes the device data except the crash reporting consent and the installation marker, removes the crash reporting user and shows the onboarding page',
        (tester) async {
          final container = await openSettingsWithStoredPreferences(
            tester,
            isCrashReportingEnabled: true,
          );
          final requests = recordAuthenticationRequests(
            container,
            respondFromApi: (_) async => ResponseBody.fromString('', 204),
          );

          await confirmDeletion(tester);

          final deletion = requests.api.requests.single;
          expect(deletion.method, 'DELETE');
          expect(deletion.uri.path, '/v1/users/me');
          expect(
            deletion.headers['Authorization'],
            'Bearer $issuedJsonWebToken',
          );
          await expectDeviceDataRemovedOnOnboarding(
            tester,
            isCrashReportingEnabled: true,
          );
          expect(
            (container.read(
              crashReporterProvider,
            ) as RecordingCrashReporter).calls.last,
            'user none',
          );
        },
      );

      testWidgets(
        'confirming the deletion when auth rejects the session on the token renewal removes the device data except the crash reporting opt-out and the installation marker and shows the onboarding page',
        (tester) async {
          final container = await openSettingsWithStoredPreferences(
            tester,
            isCrashReportingEnabled: false,
          );
          recordAuthenticationRequests(
            container,
            respondFromAuth: (request) async => request.uri.path == tokenPath
                ? ResponseBody.fromString('', 401)
                : await respondAsAuth(request),
            respondFromApi: (_) async => ResponseBody.fromString('', 401),
          );

          await confirmDeletion(tester);

          expect(container.read(signedInAccountProvider), isNull);
          await expectDeviceDataRemovedOnOnboarding(
            tester,
            isCrashReportingEnabled: false,
          );
        },
      );

      testWidgets(
        'failing deletion shows the error dialog and keeps the account and the device data, confirming again deletes the account',
        (tester) async {
          final container = await openSettingsWithStoredPreferences(
            tester,
            isCrashReportingEnabled: false,
          );
          var deletionAttempts = 0;
          final requests = recordAuthenticationRequests(
            container,
            respondFromApi: (_) async => ++deletionAttempts == 1
                ? jsonResponse(const {'status': 502}, statusCode: 502)
                : ResponseBody.fromString('', 204),
          );

          await confirmDeletion(tester);

          expect(find.text(englishTranslations.apiError.title), findsOneWidget);
          expect(container.read(signedInAccountProvider), isNotNull);
          expect(
            await const FlutterSecureStorage().readAll(),
            storedSession('account'),
          );
          expect((await reopenPreferences()).keys, {
            PreferenceKeys.installed,
            PreferenceKeys.crashReportingEnabled,
            ...storedPreferencesBeforeDeletion.keys,
          });
          await tester.tap(find.text(englishTranslations.apiError.close));
          await tester.pumpAndSettle();
          expect(find.byType(SettingsPage), findsOneWidget);

          await confirmDeletion(tester);

          expect(requests.api.requests, hasLength(2));
          expect(find.byType(OnboardingPage), findsOneWidget);
        },
      );

      testWidgets(
        'tapping delete account again while the deletion is pending shows a progress indicator and sends no second request',
        (tester) async {
          final container = await openSettings(tester);
          final pendingDeletion = Completer<ResponseBody>();
          final requests = recordAuthenticationRequests(
            container,
            respondFromApi: (_) => pendingDeletion.future,
          );
          await tapRow(tester, translations.account.deleteAccount);
          await slideToDelete(tester);
          await tester.pump(const Duration(seconds: 1));

          await tester.tap(find.text(translations.account.deleteAccount));
          await tester.pump(const Duration(seconds: 1));

          expect(requests.api.requests, hasLength(1));
          expect(find.byType(AlertDialog), findsNothing);
          expect(find.byType(CircularProgressIndicator), findsOneWidget);
          pendingDeletion.complete(ResponseBody.fromString('', 204));
          await tester.pumpAndSettle();
          expect(find.byType(OnboardingPage), findsOneWidget);
        },
      );

      Future<void> deleteAccountInDevelopment(WidgetTester tester) async {
        final container = await pumpApp(
          tester,
          flavor: Flavor.development,
          signedInAccount: const Account(id: 'account'),
        );
        await tester.tap(
          find.byTooltip(germanTranslations.home.settingsTooltip),
        );
        await tester.pumpAndSettle();
        recordAuthenticationRequests(
          container,
          respondFromApi: (_) async => ResponseBody.fromString('', 204),
        );
        await tapRow(tester, translations.account.deleteAccount);
        await slideToDelete(tester);
        await tester.pumpAndSettle();
      }

      testWidgets(
        'confirming the deletion on iOS in development shows the notice to disconnect Sign in with Apple over the onboarding page',
        (tester) async {
          await deleteAccountInDevelopment(tester);

          final notice = translations.account.appleDisconnectDialog;
          expect(find.text(notice.message), findsOneWidget);
          await tester.tap(find.text(notice.confirm));
          await tester.pumpAndSettle();
          expect(find.text(notice.message), findsNothing);
          expect(find.byType(OnboardingPage), findsOneWidget);
        },
        variant: TargetPlatformVariant.only(TargetPlatform.iOS),
      );

      testWidgets(
        'confirming the deletion on Android in development shows no notice to disconnect Sign in with Apple',
        (tester) async {
          await deleteAccountInDevelopment(tester);

          expect(
            find.text(translations.account.appleDisconnectDialog.message),
            findsNothing,
          );
          expect(find.byType(OnboardingPage), findsOneWidget);
        },
        variant: TargetPlatformVariant.only(TargetPlatform.android),
      );

      testWidgets(
        'leaving and reopening the settings page while the deletion is pending keeps sign out and delete account disabled',
        (tester) async {
          final container = await openSettings(tester);
          final pendingDeletion = Completer<ResponseBody>();
          recordAuthenticationRequests(
            container,
            respondFromApi: (_) => pendingDeletion.future,
          );
          await tapRow(tester, translations.account.deleteAccount);
          await slideToDelete(tester);
          await tester.pump(const Duration(seconds: 1));

          await tester.binding.handlePopRoute();
          await tester.pump();
          await tester.pump(const Duration(seconds: 1));
          expect(find.byType(SettingsPage), findsNothing);
          await tester.tap(
            find.byTooltip(germanTranslations.home.settingsTooltip),
          );
          await tester.pump();
          await tester.pump(const Duration(seconds: 1));
          await tester.scrollUntilVisible(
            find.text(translations.account.deleteAccount),
            100,
          );

          for (final label in [
            translations.account.signOut,
            translations.account.deleteAccount,
          ]) {
            expect(
              tester
                  .widget<ListTile>(find.widgetWithText(ListTile, label))
                  .onTap,
              isNull,
            );
          }
          pendingDeletion.complete(ResponseBody.fromString('', 204));
          await tester.pumpAndSettle();
          expect(find.byType(OnboardingPage), findsOneWidget);
        },
      );
    });

    testWidgets('about section shows the app version', (tester) async {
      await openSettings(tester);

      await tester.scrollUntilVisible(find.text('1.2.3 (45)'), 100);

      expect(find.text('1.2.3 (45)'), findsOneWidget);
    });
  });
}
