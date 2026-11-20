import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../authentication/signed_in_account_provider.dart';
import 'app_router.dart';
import 'sign_in_guard.dart';

part 'app_router_provider.g.dart';

@Riverpod(keepAlive: true)
AppRouter appRouter(Ref ref) => AppRouter(
  signInGuard: SignInGuard(
    isSignedIn: () => ref.read(signedInAccountProvider) != null,
  ),
);
