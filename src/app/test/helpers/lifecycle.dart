import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';

Future<void> changeLifecycleState(
  WidgetTester tester,
  AppLifecycleState state,
) => tester.binding.defaultBinaryMessenger.handlePlatformMessage(
  SystemChannels.lifecycle.name,
  SystemChannels.lifecycle.codec.encodeMessage(state.toString()),
  (_) {},
);
