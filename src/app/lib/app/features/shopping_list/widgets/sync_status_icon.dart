import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';
import 'package:powersync/powersync.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/sync/sync_status_provider.dart';
import '../../../core/theme/app_colors.dart';

class SyncStatusIcon extends ConsumerWidget {
  const SyncStatusIcon({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final translations = context.t.shoppingList;
    final (icon, label) = switch (ref.watch(syncStatusProvider)) {
      AsyncData(value: SyncStatus(uploading: true)) => (
        Icons.cloud_upload_outlined,
        translations.syncUploading,
      ),
      AsyncData(value: SyncStatus(connected: true)) => (
        Icons.cloud_done_outlined,
        translations.syncConnected,
      ),
      AsyncError() => (Icons.cloud_off_outlined, translations.syncUnavailable),
      _ => (Icons.cloud_off_outlined, translations.syncOffline),
    };
    return Tooltip(
      message: label,
      child: Icon(
        icon,
        color: context.colors.inkSecondary,
        semanticLabel: label,
      ),
    );
  }
}
