import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:app/app/core/flavor/flavor.dart';
import 'package:app/app/core/flavor/flavor_provider.dart';
import 'package:app/app/core/storage/shared_preferences_provider.dart';

ProviderContainer createContainer(
  SharedPreferencesWithCache preferences, {
  Flavor? flavor,
}) => ProviderContainer.test(
  overrides: [
    sharedPreferencesProvider.overrideWithValue(preferences),
    if (flavor != null) flavorProvider.overrideWithValue(flavor),
  ],
);
