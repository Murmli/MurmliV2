import 'package:dio/dio.dart';
import 'package:powersync/powersync.dart';
import 'package:talker/talker.dart';

class MurmliBackendConnector extends PowerSyncBackendConnector {
  MurmliBackendConnector({
    required this.syncEndpoint,
    required this.apiClient,
    required this.fetchJsonWebToken,
    required this.talker,
  });

  final Uri syncEndpoint;
  final Dio apiClient;
  final Future<String?> Function() fetchJsonWebToken;
  final Talker talker;

  @override
  Future<PowerSyncCredentials?> fetchCredentials() async {
    final jsonWebToken = await fetchJsonWebToken();
    if (jsonWebToken == null) {
      return null;
    }
    return PowerSyncCredentials(
      endpoint: syncEndpoint.toString(),
      token: jsonWebToken,
    );
  }

  @override
  Future<void> uploadData(PowerSyncDatabase database) async {
    final transaction = await database.getNextCrudTransaction();
    if (transaction == null) {
      return;
    }
    final jsonWebToken = await fetchJsonWebToken();
    if (jsonWebToken == null) {
      throw StateError('Cannot upload sync changes while signed out.');
    }
    try {
      final response = await apiClient.post<Map<String, Object?>>(
        '/v1/sync/upload',
        data: {
          'operations': [for (final entry in transaction.crud) entry.toJson()],
        },
        options: Options(headers: {'Authorization': 'Bearer $jsonWebToken'}),
      );
      _logRejectedResults(response.data);
    } on DioException catch (error) {
      // A malformed batch never succeeds, retrying it would block every later
      // change.
      if (error.response?.statusCode != 400) {
        rethrow;
      }
      talker.warning('Sync upload discarded a batch the API rejected with 400');
    }
    await transaction.complete();
  }

  void _logRejectedResults(Map<String, Object?>? body) {
    if (body case {'results': final List<Object?> results}) {
      for (final result in results) {
        if (result case {
          'status': 'rejected',
          'type': final String table,
          'id': final String id,
        }) {
          final reason = switch (result) {
            {'reason': final String reason} => reason,
            _ => 'no reason',
          };
          talker.warning('Sync upload rejected $table/$id: $reason');
        }
      }
    }
  }
}
