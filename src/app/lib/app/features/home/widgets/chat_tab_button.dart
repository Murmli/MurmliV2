import 'package:material_ui/material_ui.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_motion.dart';
import '../../../core/theme/app_spacing.dart';
import '../home_tab.dart';

class ChatTabButton extends StatelessWidget {
  const ChatTabButton({
    required this.isActive,
    required this.onPressed,
    super.key,
  });

  final bool isActive;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = context.colors;
    return Semantics(
      button: true,
      selected: isActive,
      label: HomeTab.chat.label(context.t),
      excludeSemantics: true,
      onTap: onPressed,
      child: InkWell(
        onTap: onPressed,
        borderRadius: BorderRadius.circular(AppRadius.navigationTab),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minWidth: 72, minHeight: 60),
          child: Center(
            heightFactor: 1,
            child: Transform.translate(
              offset: const Offset(0, -9),
              child: AnimatedContainer(
                duration: AppMotion.short(context),
                width: 56,
                height: 56,
                decoration: BoxDecoration(
                  color: colors.primary,
                  shape: BoxShape.circle,
                  boxShadow: [
                    BoxShadow(
                      color: colors.primary.withValues(alpha: 0.55),
                      offset: const Offset(0, 10),
                      blurRadius: 20,
                      spreadRadius: -6,
                    ),
                    BoxShadow(
                      color: isActive ? colors.primarySoft : Colors.transparent,
                      spreadRadius: 4,
                    ),
                  ],
                ),
                child: Icon(
                  HomeTab.chat.icon,
                  size: 26,
                  color: colors.onPrimary,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
