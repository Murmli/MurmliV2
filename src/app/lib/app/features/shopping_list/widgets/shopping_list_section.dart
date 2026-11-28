import 'dart:async';

import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';
import '../models/shopping_list_item.dart';
import '../providers/recently_toggled_item_provider.dart';
import 'shopping_list_pinned_header.dart';

class ShoppingListSection extends HookConsumerWidget {
  const ShoppingListSection({
    required this.items,
    required this.leavingItemIds,
    required this.headerContentHeight,
    required this.header,
    required this.body,
    super.key,
  });

  final List<ShoppingListItem> items;
  final Set<String> leavingItemIds;
  final double headerContentHeight;
  final Widget header;
  final Widget? body;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final leaves = items.every((item) => leavingItemIds.contains(item.id));
    final appears = useMemoized(
      () =>
          !leaves &&
          items.length == 1 &&
          ref.read(recentlyToggledItemProvider) == items.single.id,
    );
    final visibility = useAnimationController(
      duration: AppMotion.short(context),
      initialValue: appears ? 0 : 1,
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
      unawaited(leaves ? visibility.reverse() : visibility.forward());
      return null;
    }, [leaves]);
    final visibleFraction = useAnimation(curvedVisibility);
    final body = this.body;
    return SliverFadeTransition(
      opacity: curvedVisibility,
      sliver: SliverMainAxisGroup(
        slivers: [
          ShoppingListPinnedHeader(
            contentHeight: headerContentHeight,
            visibleFraction: visibleFraction,
            child: header,
          ),
          SliverPadding(
            padding: EdgeInsetsDirectional.only(
              start: AppSpacing.screenMargin,
              end: AppSpacing.screenMargin,
              bottom: AppSpacing.space12 * visibleFraction,
            ),
            sliver: body == null
                ? null
                : SliverToBoxAdapter(
                    child: SizeTransition(
                      sizeFactor: curvedVisibility,
                      alignment: AlignmentDirectional.topCenter,
                      child: body,
                    ),
                  ),
          ),
        ],
      ),
    );
  }
}
