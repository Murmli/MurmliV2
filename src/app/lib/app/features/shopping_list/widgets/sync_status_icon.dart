import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/sync/sync_engine_provider.dart';
import '../../../core/theme/app_colors.dart';

class SyncStatusIcon extends ConsumerWidget {
  const SyncStatusIcon({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.shoppingList;
    final (icon, label) = switch (ref.watch(syncEngineStatusProvider)) {
      AsyncData(value: (isOffline: true, isConnected: _, isSyncing: _)) => (
        Icons.cloud_off_outlined,
        translations.syncOffline,
      ),
      AsyncData(value: (isSyncing: true, isConnected: _, isOffline: _)) => (
        Icons.cloud_sync_outlined,
        translations.syncing,
      ),
      AsyncData(value: (isConnected: true, isSyncing: _, isOffline: _)) => (
        Icons.cloud_done_outlined,
        translations.syncConnected,
      ),
      AsyncData() => (Icons.cloud_outlined, translations.syncConnecting),
      AsyncError() => (Icons.cloud_off_outlined, translations.syncUnavailable),
      _ => (Icons.cloud_off_outlined, translations.syncOffline),
    };
    final pendingCommandCount =
        ref.watch(pendingCommandCountProvider).value ?? 0;
    final message = pendingCommandCount == 0
        ? label
        : '$label\n${translations.syncPending(count: pendingCommandCount)}';
    return Tooltip(
      message: message,
      child: Badge.count(
        count: pendingCommandCount,
        isLabelVisible: pendingCommandCount > 0,
        child: Icon(
          icon,
          color: context.colors.inkSecondary,
          semanticLabel: message,
        ),
      ),
    );
  }
}
