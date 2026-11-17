enum Flavor {
  local(bannerLabel: 'local'),
  development(bannerLabel: 'dev'),
  production(bannerLabel: null);

  const Flavor({required this.bannerLabel});

  final String? bannerLabel;
}
