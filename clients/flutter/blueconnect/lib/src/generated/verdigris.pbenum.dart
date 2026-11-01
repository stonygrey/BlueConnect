// This is a generated file - do not edit.
//
// Generated from verdigris.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports

import 'dart:core' as $core;

import 'package:protobuf/protobuf.dart' as $pb;

class Status extends $pb.ProtobufEnum {
  static const Status STATUS_OK =
      Status._(0, _omitEnumNames ? '' : 'STATUS_OK');
  static const Status STATUS_UNKNOWN_OP =
      Status._(1, _omitEnumNames ? '' : 'STATUS_UNKNOWN_OP');
  static const Status STATUS_INVALID_ARGUMENT =
      Status._(2, _omitEnumNames ? '' : 'STATUS_INVALID_ARGUMENT');
  static const Status STATUS_OUT_OF_RANGE =
      Status._(3, _omitEnumNames ? '' : 'STATUS_OUT_OF_RANGE');
  static const Status STATUS_INTERNAL =
      Status._(4, _omitEnumNames ? '' : 'STATUS_INTERNAL');
  static const Status STATUS_BUSY =
      Status._(5, _omitEnumNames ? '' : 'STATUS_BUSY');

  /// The session is not authenticated (no pairing matches the session key),
  /// or the pairing ceremony was not confirmed on the device.
  static const Status STATUS_UNAUTHENTICATED =
      Status._(6, _omitEnumNames ? '' : 'STATUS_UNAUTHENTICATED');

  /// The session is authenticated but its privilege tier does not permit
  /// this command.
  static const Status STATUS_PERMISSION_DENIED =
      Status._(7, _omitEnumNames ? '' : 'STATUS_PERMISSION_DENIED');

  static const $core.List<Status> values = <Status>[
    STATUS_OK,
    STATUS_UNKNOWN_OP,
    STATUS_INVALID_ARGUMENT,
    STATUS_OUT_OF_RANGE,
    STATUS_INTERNAL,
    STATUS_BUSY,
    STATUS_UNAUTHENTICATED,
    STATUS_PERMISSION_DENIED,
  ];

  static final $core.List<Status?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 7);
  static Status? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const Status._(super.value, super.name);
}

/// Privilege tier granted by a pairing ceremony and bound to the client key.
class Privilege extends $pb.ProtobufEnum {
  static const Privilege PRIVILEGE_LOW =
      Privilege._(0, _omitEnumNames ? '' : 'PRIVILEGE_LOW');
  static const Privilege PRIVILEGE_HIGH =
      Privilege._(1, _omitEnumNames ? '' : 'PRIVILEGE_HIGH');

  static const $core.List<Privilege> values = <Privilege>[
    PRIVILEGE_LOW,
    PRIVILEGE_HIGH,
  ];

  static final $core.List<Privilege?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 1);
  static Privilege? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const Privilege._(super.value, super.name);
}

const $core.bool _omitEnumNames =
    $core.bool.fromEnvironment('protobuf.omit_enum_names');
