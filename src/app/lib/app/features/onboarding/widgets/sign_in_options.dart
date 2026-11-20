import 'dart:async';

import 'package:auto_route/auto_route.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_hooks/flutter_hooks.dart';
import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:material_ui/material_ui.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import '../../../../i18n/strings.g.dart';
import '../../../core/authentication/signed_in_account_provider.dart';
import '../../../core/flavor/flavor.dart';
import '../../../core/flavor/flavor_provider.dart';
import '../../../core/router/app_router.dart';
import '../../../core/theme/app_spacing.dart';

const _appleButtonHeight = 44.0;
const _googleLogoSize = 20.0;

class SignInOptions extends ConsumerWidget {
  const SignInOptions({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => switch (ref.watch(
    flavorProvider,
  )) {
    Flavor.local => const _EmailSignInForm(),
    Flavor.development || Flavor.production => switch (defaultTargetPlatform) {
      TargetPlatform.iOS => const _AppleSignInButton(),
      TargetPlatform.android => const _GoogleSignInButton(),
      _ => const SizedBox.shrink(),
    },
  };
}

Future<void> _signIn(
  BuildContext context,
  ValueNotifier<bool> isSigningIn,
  Future<SignInResult> Function() signIn,
) async {
  isSigningIn.value = true;
  final SignInResult result;
  try {
    result = await signIn();
  } on Object {
    if (context.mounted) {
      isSigningIn.value = false;
    }
    rethrow;
  }
  if (!context.mounted) {
    return;
  }
  final translations = context.t.onboarding;
  switch (result) {
    case SignInResult.signedIn:
      await context.router.replaceAll([const HomeRoute()]);
    case SignInResult.failed:
      isSigningIn.value = false;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(translations.signInFailed)));
    case SignInResult.invalidCredentials:
      isSigningIn.value = false;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(translations.emailSignIn.invalidCredentials)),
      );
    case SignInResult.canceled || SignInResult.apiRequestFailed:
      isSigningIn.value = false;
  }
}

class _AppleSignInButton extends HookConsumerWidget {
  const _AppleSignInButton();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isSigningIn = useState(false);
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
      onPressed: () => unawaited(
        _signIn(
          context,
          isSigningIn,
          ref.read(signedInAccountProvider.notifier).signInWithApple,
        ),
      ),
    );
  }
}

class _GoogleSignInButton extends HookConsumerWidget {
  const _GoogleSignInButton();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isSigningIn = useState(false);
    return OutlinedButton.icon(
      onPressed: isSigningIn.value
          ? null
          : () => unawaited(
              _signIn(
                context,
                isSigningIn,
                ref.read(signedInAccountProvider.notifier).signInWithGoogle,
              ),
            ),
      icon: isSigningIn.value
          ? const SizedBox.square(
              dimension: 24,
              child: CircularProgressIndicator(),
            )
          : Image.asset(
              'assets/images/google_logo.png',
              width: _googleLogoSize,
              height: _googleLogoSize,
            ),
      label: Text(context.t.onboarding.continueWithGoogle),
    );
  }
}

class _EmailSignInForm extends HookConsumerWidget {
  const _EmailSignInForm();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isSigningIn = useState(false);
    final emailController = useTextEditingController();
    final passwordController = useTextEditingController();
    final translations = context.t.onboarding.emailSignIn;

    void submit() {
      if (isSigningIn.value) {
        return;
      }
      unawaited(
        _signIn(
          context,
          isSigningIn,
          () => ref
              .read(signedInAccountProvider.notifier)
              .signInWithEmail(
                email: emailController.text.trim(),
                password: passwordController.text,
              ),
        ),
      );
    }

    return AutofillGroup(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        spacing: AppSpacing.space12,
        children: [
          TextField(
            controller: emailController,
            enabled: !isSigningIn.value,
            decoration: InputDecoration(labelText: translations.email),
            keyboardType: TextInputType.emailAddress,
            textInputAction: TextInputAction.next,
            autofillHints: const [AutofillHints.email],
            autocorrect: false,
          ),
          TextField(
            controller: passwordController,
            enabled: !isSigningIn.value,
            decoration: InputDecoration(labelText: translations.password),
            obscureText: true,
            textInputAction: TextInputAction.done,
            autofillHints: const [AutofillHints.password],
            onSubmitted: (_) => submit(),
          ),
          FilledButton(
            onPressed: isSigningIn.value ? null : submit,
            child: isSigningIn.value
                ? const SizedBox.square(
                    dimension: 24,
                    child: CircularProgressIndicator(),
                  )
                : Text(translations.submit),
          ),
        ],
      ),
    );
  }
}
