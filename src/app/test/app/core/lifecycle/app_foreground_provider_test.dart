import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';

import 'package:app/app/core/lifecycle/app_foreground_provider.dart';

import '../../../helpers/lifecycle.dart';

void main() {
  testWidgets('the app counts as in the foreground while resumed or inactive '
      'and in the background while hidden or paused', (tester) async {
    final container = ProviderContainer.test();
    final values = <bool>[];
    container.listen(
      appForegroundProvider,
      (_, next) => values.add(next),
      fireImmediately: true,
    );

    for (final state in [
      AppLifecycleState.resumed,
      AppLifecycleState.inactive,
      AppLifecycleState.hidden,
      AppLifecycleState.paused,
      AppLifecycleState.hidden,
      AppLifecycleState.inactive,
      AppLifecycleState.resumed,
    ]) {
      await changeLifecycleState(tester, state);
    }

    expect(values, [true, false, true]);
  });
}
