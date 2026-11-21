import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/sync/simulated_offline_provider.dart';
import '../../../core/theme/app_colors.dart';

class SimulatedOfflineButton extends ConsumerWidget {
  const SimulatedOfflineButton({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.shoppingList;
    final isSimulatedOffline = ref.watch(simulatedOfflineProvider);
    return IconButton(
      onPressed: ref.read(simulatedOfflineProvider.notifier).toggle,
      tooltip: isSimulatedOffline
          ? translations.simulatedOfflineActive
          : translations.simulateOffline,
      icon: isSimulatedOffline
          ? Icon(Icons.wifi_off, color: ColorScheme.of(context).error)
          : Icon(Icons.wifi, color: context.colors.inkSecondary),
    );
  }
}
