import 'package:powersync/powersync.dart';

const syncSchema = Schema([
  Table('lists', [
    Column.text('name'),
    Column.text('created_at'),
    Column.text('updated_at'),
  ]),
  Table('list_members', [
    Column.text('list_id'),
    Column.text('user_id'),
    Column.text('created_at'),
  ]),
  Table('items', [
    Column.text('list_id'),
    Column.text('name'),
    Column.real('quantity'),
    Column.text('unit'),
    Column.text('category'),
    Column.integer('checked'),
    Column.text('created_at'),
    Column.text('updated_at'),
  ]),
]);
