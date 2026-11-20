import 'package:dart_mappable/dart_mappable.dart';

part 'sign_in_models.mapper.dart';

@MappableEnum()
enum SocialProvider { apple, google }

@MappableClass()
class IdTokenRequest with IdTokenRequestMappable {
  const IdTokenRequest({required this.token, required this.nonce});

  final String token;
  final String nonce;
}

@MappableClass()
class SocialSignInRequest with SocialSignInRequestMappable {
  const SocialSignInRequest({required this.provider, required this.idToken});

  final SocialProvider provider;
  final IdTokenRequest idToken;
}

@MappableClass()
class TokenResponse with TokenResponseMappable {
  const TokenResponse({required this.token});

  final String token;
}

@MappableClass()
class UserResponse with UserResponseMappable {
  const UserResponse({required this.id});

  final String id;
}
