import 'package:flutter_test/flutter_test.dart';

import 'package:app/app/features/home/floating_navigation_bar.dart';
import 'package:app/app/features/home/home_tab.dart';
import 'package:app/app/features/home/tabs/chat_page.dart';

import '../../../helpers/pump_app.dart';

void main() {
  group('AppRouter', () {
    testWidgets('start shows home with the chat tab active', (tester) async {
      await pumpApp(tester);

      expect(find.byType(ChatPage), findsOneWidget);
      expect(
        tester
            .widget<FloatingNavigationBar>(find.byType(FloatingNavigationBar))
            .activeTab,
        HomeTab.chat,
      );
    });
  });
}
