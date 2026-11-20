import 'package:flutter_test/flutter_test.dart';
import 'package:sign_in_with_apple_platform_interface/sign_in_with_apple_platform_interface.dart';

const appleIdentityToken = 'apple-identity-token';

class FakeSignInWithApplePlatform extends SignInWithApplePlatform {
  FakeSignInWithApplePlatform({required this.credential, this.exception});

  final AuthorizationCredentialAppleID credential;
  final Exception? exception;
  List<AppleIDAuthorizationScopes>? receivedScopes;
  String? receivedNonce;

  @override
  Future<AuthorizationCredentialAppleID> getAppleIDCredential({
    required List<AppleIDAuthorizationScopes> scopes,
    WebAuthenticationOptions? webAuthenticationOptions,
    String? nonce,
    String? state,
  }) async {
    receivedScopes = scopes;
    receivedNonce = nonce;
    if (exception case final exception?) {
      throw exception;
    }
    return credential;
  }
}

FakeSignInWithApplePlatform installFakeSignInWithApple({
  AuthorizationCredentialAppleID credential =
      const AuthorizationCredentialAppleID(
        userIdentifier: 'apple-user',
        givenName: null,
        familyName: null,
        authorizationCode: 'apple-authorization-code',
        email: null,
        identityToken: appleIdentityToken,
        state: null,
      ),
  Exception? exception,
}) {
  final previousPlatform = SignInWithApplePlatform.instance;
  addTearDown(() => SignInWithApplePlatform.instance = previousPlatform);
  return SignInWithApplePlatform.instance = FakeSignInWithApplePlatform(
    credential: credential,
    exception: exception,
  );
}
