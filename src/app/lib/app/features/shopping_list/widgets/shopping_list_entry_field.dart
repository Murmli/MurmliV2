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
import '../shopping_list_write.dart';
import 'shopping_list_progress_counter.dart';

class ShoppingListEntryField extends HookConsumerWidget {
  const ShoppingListEntryField({
    required this.listId,
    required this.checkedCount,
    required this.totalCount,
    super.key,
  });

  final String listId;
  final int checkedCount;
  final int totalCount;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = context.colors;
    final textTheme = TextTheme.of(context);
    final controller = useTextEditingController();
    final hasText = useValueListenable(controller).text.trim().isNotEmpty;
    void submit() {
      final text = controller.text;
      unawaited(
        runShoppingListWrite(
          context,
          () => ref
              .read(pendingShoppingListEntriesProvider.notifier)
              .submit(listId: listId, text: text),
        ),
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
        padding: const EdgeInsetsDirectional.only(start: 20, end: 5),
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
            const SizedBox(width: AppSpacing.space4),
            if (totalCount > 0)
              _CollapsingProgressCounter(
                visible: !hasText,
                checkedCount: checkedCount,
                totalCount: totalCount,
              ),
            _RoundButton(
              icon: Icons.attach_file,
              label: context.t.shoppingList.entry.attach,
              isActive: false,
            ),
            const SizedBox(width: AppSpacing.space4),
            _RoundButton(
              icon: Icons.arrow_upward_rounded,
              label: context.t.shoppingList.entry.add,
              isActive: hasText,
              onPressed: hasText ? submit : null,
            ),
          ],
        ),
      ),
    );
  }
}

class _CollapsingProgressCounter extends HookWidget {
  const _CollapsingProgressCounter({
    required this.visible,
    required this.checkedCount,
    required this.totalCount,
  });

  final bool visible;
  final int checkedCount;
  final int totalCount;

  @override
  Widget build(BuildContext context) {
    final textDirection = Directionality.of(context);
    final visibility = useAnimationController(
      duration: AppMotion.short(context),
      initialValue: visible ? 1 : 0,
    );
    final curvedVisibility = useMemoized(
      () => CurvedAnimation(
        parent: visibility,
        curve: Curves.easeOut,
        reverseCurve: Curves.easeIn,
      ),
      [visibility],
    );
    useEffect(() => curvedVisibility.dispose, [curvedVisibility]);
    useEffect(() {
      unawaited(visible ? visibility.forward() : visibility.reverse());
      return null;
    }, [visible]);
    return ExcludeSemantics(
      excluding: !visible,
      child: IgnorePointer(
        ignoring: !visible,
        child: SizeTransition(
          axis: Axis.horizontal,
          alignment: AlignmentDirectional.centerEnd,
          sizeFactor: curvedVisibility,
          child: Padding(
            padding: const EdgeInsetsDirectional.only(end: AppSpacing.space4),
            child: SlideTransition(
              position: Tween(
                begin: const Offset(0.5, 0),
                end: Offset.zero,
              ).animate(curvedVisibility),
              textDirection: textDirection,
              child: ScaleTransition(
                scale: curvedVisibility,
                alignment: AlignmentDirectional.centerEnd.resolve(
                  textDirection,
                ),
                child: FadeTransition(
                  opacity: curvedVisibility,
                  child: ShoppingListProgressCounter(
                    checkedCount: checkedCount,
                    totalCount: totalCount,
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _RoundButton extends StatelessWidget {
  const _RoundButton({
    required this.icon,
    required this.label,
    required this.isActive,
    this.onPressed,
  });

  final IconData icon;
  final String label;
  final bool isActive;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Tooltip(
      message: label,
      excludeFromSemantics: true,
      child: Semantics(
        container: true,
        button: true,
        enabled: onPressed != null,
        label: label,
        onTap: onPressed,
        excludeSemantics: true,
        child: TweenAnimationBuilder<double>(
          tween: Tween(end: isActive ? 1 : 0),
          duration: AppMotion.short(context),
          curve: Curves.easeOut,
          builder: (context, activity, _) => Material(
            color: Color.lerp(
              colors.surfaceSecondary,
              colors.primary,
              activity,
            ),
            shape: const CircleBorder(),
            clipBehavior: Clip.antiAlias,
            child: InkWell(
              onTap: onPressed,
              child: SizedBox.square(
                dimension: 42,
                child: Icon(
                  icon,
                  size: 20,
                  color: Color.lerp(
                    colors.inkTertiary,
                    colors.onPrimary,
                    activity,
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
