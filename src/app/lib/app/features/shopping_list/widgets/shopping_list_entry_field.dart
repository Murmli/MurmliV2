import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';
import '../providers/pending_shopping_list_entries_provider.dart';

class ShoppingListEntryField extends HookConsumerWidget {
  const ShoppingListEntryField({required this.listId, super.key});

  final String listId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final controller = useTextEditingController();
    final hasText = useValueListenable(controller).text.trim().isNotEmpty;
    void submit() {
      unawaited(
        ref
            .read(pendingShoppingListEntriesProvider.notifier)
            .submit(listId: listId, text: controller.text),
      );
      controller.clear();
    }

    return Padding(
      padding: const EdgeInsetsDirectional.only(
        start: AppSpacing.screenMargin,
        end: AppSpacing.screenMargin,
        bottom: AppSpacing.space12,
      ),
      child: Container(
        height: 52,
        padding: const EdgeInsetsDirectional.only(start: 22, end: 5),
        decoration: ShapeDecoration(
          shape: const StadiumBorder(),
          color: colors.surface,
          shadows: colors.cardShadow,
        ),
        child: Row(
          children: [
            Expanded(
              child: TextField(
                controller: controller,
                decoration: InputDecoration.collapsed(
                  hintText: context.t.shoppingList.entry.hint,
                  hintStyle: textTheme.bodyMedium?.copyWith(
                    color: colors.inkTertiary,
                  ),
                ),
                style: textTheme.bodyMedium,
                textInputAction: TextInputAction.done,
                textCapitalization: TextCapitalization.sentences,
                inputFormatters: [LengthLimitingTextInputFormatter(256)],
                onTapOutside: (_) =>
                    FocusManager.instance.primaryFocus?.unfocus(),
                onEditingComplete: submit,
              ),
            ),
            AnimatedSwitcher(
              duration: AppMotion.short(context),
              child: hasText
                  ? _AddButton(onTap: submit)
                  : const _MicrophoneIcon(),
            ),
          ],
        ),
      ),
    );
  }
}

class _MicrophoneIcon extends StatelessWidget {
  const _MicrophoneIcon();

  @override
  Widget build(BuildContext context) => const ExcludeSemantics(
    child: _ButtonCircle(child: Icon(Icons.mic_none_rounded, size: 22)),
  );
}

class _AddButton extends StatelessWidget {
  const _AddButton({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Tooltip(
    message: context.t.shoppingList.entry.add,
    child: _ButtonCircle(
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          customBorder: const CircleBorder(),
          onTap: onTap,
          child: const Center(
            child: Icon(Icons.arrow_upward_rounded, size: 22),
          ),
        ),
      ),
    ),
  );
}

class _ButtonCircle extends StatelessWidget {
  const _ButtonCircle({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Container(
      width: 42,
      height: 42,
      decoration: BoxDecoration(color: colors.primary, shape: BoxShape.circle),
      child: IconTheme.merge(
        data: IconThemeData(color: colors.onPrimary),
        child: child,
      ),
    );
  }
}
