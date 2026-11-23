import 'package:dio/dio.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import 'package:app/app/core/api/api_client_provider.dart';
import 'package:app/i18n/strings.g.dart';

import '../../../helpers/authentication.dart';
import '../../../helpers/http_client_adapter.dart';
import '../../../helpers/pump_app.dart';

void main() {
  final translations = AppLocale.de.buildSync().requestFailure;
  late ProviderContainer container;
  late RecordingHttpClientAdapter adapter;

  Future<void> pumpAppWithFailingApi(WidgetTester tester) async {
    container = await pumpApp(tester);
    await tester.runAsync(() => signIn(container));
    await tester.pumpAndSettle();
    adapter = RecordingHttpClientAdapter(
      respond: (_) async => ResponseBody.fromString('', 500),
    );
    container.read(apiClientProvider).httpClientAdapter = adapter;
  }

  Future<void> failRequest(WidgetTester tester) async {
    final request = expectLater(
      container.read(apiClientProvider).get<void>('/v1/example'),
      throwsA(isA<DioException>()),
    );
    await tester.pumpAndSettle();
    await request;
  }

  Future<void> changeLifecycleState(
    WidgetTester tester,
    AppLifecycleState state,
  ) => tester.binding.defaultBinaryMessenger.handlePlatformMessage(
    SystemChannels.lifecycle.name,
    SystemChannels.lifecycle.codec.encodeMessage(state.toString()),
    (_) {},
  );

  group('RequestFailureListener', () {
    testWidgets(
      'request to the API failing with 500 shows the server error popup with its trace id as support code',
      (tester) async {
        await pumpAppWithFailingApi(tester);

        await failRequest(tester);

        expect(find.byType(AlertDialog), findsOneWidget);
        expect(find.text(translations.serverError.title), findsOneWidget);
        expect(find.text(translations.serverError.message), findsOneWidget);
        expect(find.text(sentTraceId(adapter.requests.single)), findsOneWidget);
      },
    );

    testWidgets(
      'further failures while the popup is open show no second popup',
      (tester) async {
        await pumpAppWithFailingApi(tester);

        await failRequest(tester);
        await failRequest(tester);

        expect(find.byType(AlertDialog), findsOneWidget);
        expect(find.text(sentTraceId(adapter.requests.first)), findsOneWidget);
      },
    );

    testWidgets(
      'failure after closing the popup shows a new popup with the new trace id',
      (tester) async {
        await pumpAppWithFailingApi(tester);
        await failRequest(tester);
        await tester.tap(find.text(translations.close));
        await tester.pumpAndSettle();

        await failRequest(tester);

        expect(find.byType(AlertDialog), findsOneWidget);
        expect(find.text(sentTraceId(adapter.requests.last)), findsOneWidget);
      },
    );

    testWidgets(
      'failure while the app is in the background shows no popup after returning',
      (tester) async {
        await pumpAppWithFailingApi(tester);
        await changeLifecycleState(tester, AppLifecycleState.paused);

        await failRequest(tester);
        await changeLifecycleState(tester, AppLifecycleState.resumed);
        await tester.pumpAndSettle();

        expect(find.byType(AlertDialog), findsNothing);
      },
    );
  });
}
