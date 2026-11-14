import 'package:hooks_riverpod/hooks_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:app/app/core/storage/shared_preferences_provider.dart';

ProviderContainer createContainer(SharedPreferencesWithCache preferences) =>
    ProviderContainer.test(
      overrides: [sharedPreferencesProvider.overrideWithValue(preferences)],
    );
