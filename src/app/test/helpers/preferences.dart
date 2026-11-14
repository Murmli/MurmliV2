import 'package:shared_preferences/shared_preferences.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

import 'package:app/app/core/storage/preference_keys.dart';

Future<SharedPreferencesWithCache> createPreferences([
  Map<String, Object> storedValues = const {},
]) {
  SharedPreferencesAsyncPlatform.instance =
      InMemorySharedPreferencesAsync.withData(storedValues);
  return reopenPreferences();
}

Future<SharedPreferencesWithCache> reopenPreferences() =>
    SharedPreferencesWithCache.create(
      cacheOptions: const SharedPreferencesWithCacheOptions(
        allowList: PreferenceKeys.all,
      ),
    );
