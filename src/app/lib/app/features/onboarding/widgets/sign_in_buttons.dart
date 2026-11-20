import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/router/app_router.dart';

const _appleButtonHeight = 44.0;

class SignInButtons extends HookConsumerWidget {
  const SignInButtons({super.key});

  Future<void> _signInWithApple(
    BuildContext context,
    WidgetRef ref,
    ValueNotifier<bool> isSigningIn,
  ) async {
    isSigningIn.value = true;
    final SignInResult result;
    try {
      result = await ref
          .read(signedInAccountProvider.notifier)
          .signInWithApple();
    } on Object {
      if (context.mounted) {
        isSigningIn.value = false;
      }
      rethrow;
    }
    if (!context.mounted) {
      return;
    }
    switch (result) {
      case SignInResult.signedIn:
        await context.router.replaceAll([const HomeRoute()]);
      case SignInResult.failed:
        isSigningIn.value = false;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(context.t.onboarding.signInFailed)),
        );
      case SignInResult.canceled || SignInResult.apiRequestFailed:
        isSigningIn.value = false;
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isSigningIn = useState(false);
    if (defaultTargetPlatform != TargetPlatform.iOS) {
      return const SizedBox.shrink();
    }
    if (isSigningIn.value) {
      return const SizedBox(
        height: _appleButtonHeight,
        child: Center(child: CircularProgressIndicator()),
      );
    }
    return SignInWithAppleButton(
      text: context.t.onboarding.continueWithApple,
      height: _appleButtonHeight,
      style: Theme.of(context).brightness == Brightness.dark
          ? SignInWithAppleButtonStyle.white
          : SignInWithAppleButtonStyle.black,
      onPressed: () => unawaited(_signInWithApple(context, ref, isSigningIn)),
    );
  }
}
