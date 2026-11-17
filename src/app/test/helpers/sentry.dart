import 'package:flutter_test/flutter_test.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

const recordingSentryDsn = 'https://public@sentry.invalid/1';

Future<List<SentryEnvelope>> startRecordingSentry({
  void Function(SentryOptions options)? configure,
}) async {
  final envelopes = <SentryEnvelope>[];
  await Sentry.init((options) {
    options.dsn = recordingSentryDsn;
    configure?.call(options);
    options.transport = _RecordingTransport(envelopes);
  });
  addTearDown(Sentry.close);
  return envelopes;
}

class _RecordingTransport implements Transport {
  _RecordingTransport(this.envelopes);

  final List<SentryEnvelope> envelopes;

  @override
  Future<SentryId?> send(SentryEnvelope envelope) async {
    envelopes.add(envelope);
    return envelope.header.eventId;
  }
}
