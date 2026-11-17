import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../flavor/flavor_provider.dart';

class FlavorBanner extends ConsumerWidget {
  const FlavorBanner({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final bannerLabel = ref.watch(flavorProvider).bannerLabel;
    if (bannerLabel == null) {
      return child;
    }
    return Banner(
      message: bannerLabel,
      location: BannerLocation.topStart,
      child: child,
    );
  }
}
