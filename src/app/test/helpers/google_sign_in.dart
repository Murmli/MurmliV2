import 'package:flutter_test/flutter_test.dart';
import 'package:google_sign_in_platform_interface/google_sign_in_platform_interface.dart';

const googleIdToken = 'google-id-token';

class FakeGoogleSignInPlatform extends GoogleSignInPlatform {
  FakeGoogleSignInPlatform({required this.idToken, this.exception});

  final String? idToken;
  final Exception? exception;
  final receivedInitParameters = <InitParameters>[];
  var authenticateCalls = 0;

  @override
  Future<void> init(InitParameters params) async {
    receivedInitParameters.add(params);
  }

  @override
  Future<AuthenticationResults> authenticate(
    AuthenticateParameters params,
  ) async {
    authenticateCalls++;
    if (exception case final exception?) {
      throw exception;
    }
    return AuthenticationResults(
      user: const GoogleSignInUserData(
        email: 'google-user@murmli.de',
        id: 'google-user',
      ),
      authenticationTokens: AuthenticationTokenData(idToken: idToken),
    );
  }

  @override
  Future<AuthenticationResults?> attemptLightweightAuthentication(
    AttemptLightweightAuthenticationParameters params,
  ) => throw UnimplementedError();

  @override
  bool supportsAuthenticate() => throw UnimplementedError();

  @override
  bool authorizationRequiresUserInteraction() => throw UnimplementedError();

  @override
  Future<ClientAuthorizationTokenData?> clientAuthorizationTokensForScopes(
    ClientAuthorizationTokensForScopesParameters params,
  ) => throw UnimplementedError();

  @override
  Future<ServerAuthorizationTokenData?> serverAuthorizationTokensForScopes(
    ServerAuthorizationTokensForScopesParameters params,
  ) => throw UnimplementedError();

  @override
  Future<void> signOut(SignOutParams params) => throw UnimplementedError();

  @override
  Future<void> disconnect(DisconnectParams params) =>
      throw UnimplementedError();
}

FakeGoogleSignInPlatform installFakeGoogleSignIn({
  String? idToken = googleIdToken,
  Exception? exception,
}) {
  final previousPlatform = GoogleSignInPlatform.instance;
  addTearDown(() => GoogleSignInPlatform.instance = previousPlatform);
  return GoogleSignInPlatform.instance = FakeGoogleSignInPlatform(
    idToken: idToken,
    exception: exception,
  );
}
