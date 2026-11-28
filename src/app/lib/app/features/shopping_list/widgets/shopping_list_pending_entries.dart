import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

class ShoppingListPendingEntries extends StatelessWidget {
  const ShoppingListPendingEntries({required this.texts, super.key});

  final List<String> texts;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Padding(
      padding: const EdgeInsetsDirectional.only(
        start: AppSpacing.screenMargin,
        end: AppSpacing.screenMargin,
        bottom: AppSpacing.space12,
      ),
      child: Container(
        padding: const EdgeInsetsDirectional.only(
          start: 4,
          end: AppSpacing.space16,
        ),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppRadius.listGroup),
          boxShadow: colors.cardShadow,
        ),
        child: Column(
          children: [
            for (final (index, text) in texts.indexed) ...[
              if (index > 0) const Divider(),
              _PendingEntryRow(text: text),
            ],
          ],
        ),
      ),
    );
  }
}

class _PendingEntryRow extends StatelessWidget {
  const _PendingEntryRow({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    label: context.t.shoppingList.entry.processing(text: text),
    excludeSemantics: true,
    child: ConstrainedBox(
      constraints: const BoxConstraints(minHeight: 48),
      child: Row(
        spacing: 8,
        children: [
          const SizedBox(
            width: 48,
            child: Center(
              child: SizedBox.square(
                dimension: 20,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            ),
          ),
          Expanded(
            child: Text(
              text,
              style: TextTheme.of(context).bodyMedium
                  ?.copyWith(color: context.colors.inkSecondary),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
    ),
  );
}
