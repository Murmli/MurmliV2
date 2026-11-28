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
            if (totalCount > 0)
              _CollapsingProgressCounter(
                visible: !hasText,
                checkedCount: checkedCount,
                totalCount: totalCount,
              ),
            AnimatedSwitcher(
              duration: AppMotion.short(context),
              switchInCurve: Curves.easeOut,
              switchOutCurve: Curves.easeIn,
              transitionBuilder: (child, animation) => _ButtonIconTransition(
                incoming: child.key == ValueKey(hasText),
                animation: animation,
                child: child,
              ),
              child: hasText
                  ? _AddButton(key: const ValueKey(true), onTap: submit)
                  : const _MicrophoneIcon(key: ValueKey(false)),
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
            padding: const EdgeInsetsDirectional.symmetric(
              horizontal: AppSpacing.space8,
            ),
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

class _ButtonIconTransition extends StatelessWidget {
  const _ButtonIconTransition({
    required this.incoming,
    required this.animation,
    required this.child,
  });

  static const _turnsAway = 0.125;

  final bool incoming;
  final Animation<double> animation;
  final Widget child;

  // The outgoing icon runs the animation in reverse, so its turn starts
  // opposite to the incoming one and both icons rotate in the same direction.
  @override
  Widget build(BuildContext context) => FadeTransition(
    opacity: animation,
    child: ScaleTransition(
      scale: Tween(begin: 0.6, end: 1.0).animate(animation),
      child: RotationTransition(
        turns: Tween(
          begin: incoming ? -_turnsAway : _turnsAway,
          end: 0.0,
        ).animate(animation),
        child: child,
      ),
    ),
  );
}

class _MicrophoneIcon extends StatelessWidget {
  const _MicrophoneIcon({super.key});

  @override
  Widget build(BuildContext context) => const ExcludeSemantics(
    child: _ButtonCircle(child: Icon(Icons.mic_none_rounded, size: 22)),
  );
}

class _AddButton extends StatelessWidget {
  const _AddButton({required this.onTap, super.key});

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
