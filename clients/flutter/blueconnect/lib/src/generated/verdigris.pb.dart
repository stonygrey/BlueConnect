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

import 'package:fixnum/fixnum.dart' as $fixnum;
import 'package:protobuf/protobuf.dart' as $pb;

import 'verdigris.pbenum.dart';

export 'package:protobuf/protobuf.dart' show GeneratedMessageGenericExtensions;

export 'verdigris.pbenum.dart';

class EchoRequest extends $pb.GeneratedMessage {
  factory EchoRequest({
    $core.List<$core.int>? payload,
  }) {
    final result = EchoRequest._();
    if (payload != null) result.payload = payload;
    return result;
  }

  EchoRequest._();

  factory EchoRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      EchoRequest()..mergeFromBuffer(data, registry);
  factory EchoRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      EchoRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'EchoRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: EchoRequest.$_createMessage)
    ..a<$core.List<$core.int>>(
        1, _omitFieldNames ? '' : 'payload', $pb.PbFieldType.OY)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  EchoRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  EchoRequest copyWith(void Function(EchoRequest) updates) =>
      super.copyWith((message) => updates(message as EchoRequest))
          as EchoRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use EchoRequest() / EchoRequest.new instead')
  static EchoRequest create() => EchoRequest._();
  static $pb.GeneratedMessage $_createMessage() => EchoRequest._();
  @$core.override
  EchoRequest createEmptyInstance() => EchoRequest._();
  @$core.pragma('dart2js:noInline')
  static EchoRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<EchoRequest>(
          EchoRequest.$_createMessage);
  static EchoRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.List<$core.int> get payload => $_getN(0);
  @$pb.TagNumber(1)
  set payload($core.List<$core.int> value) => $_setBytes(0, value);
  @$pb.TagNumber(1)
  $core.bool hasPayload() => $_has(0);
  @$pb.TagNumber(1)
  void clearPayload() => $_clearField(1);
}

class EchoResponse extends $pb.GeneratedMessage {
  factory EchoResponse({
    $core.List<$core.int>? payload,
  }) {
    final result = EchoResponse._();
    if (payload != null) result.payload = payload;
    return result;
  }

  EchoResponse._();

  factory EchoResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      EchoResponse()..mergeFromBuffer(data, registry);
  factory EchoResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      EchoResponse()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'EchoResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: EchoResponse.$_createMessage)
    ..a<$core.List<$core.int>>(
        1, _omitFieldNames ? '' : 'payload', $pb.PbFieldType.OY)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  EchoResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  EchoResponse copyWith(void Function(EchoResponse) updates) =>
      super.copyWith((message) => updates(message as EchoResponse))
          as EchoResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use EchoResponse() / EchoResponse.new instead')
  static EchoResponse create() => EchoResponse._();
  static $pb.GeneratedMessage $_createMessage() => EchoResponse._();
  @$core.override
  EchoResponse createEmptyInstance() => EchoResponse._();
  @$core.pragma('dart2js:noInline')
  static EchoResponse getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<EchoResponse>(
          EchoResponse.$_createMessage);
  static EchoResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.List<$core.int> get payload => $_getN(0);
  @$pb.TagNumber(1)
  set payload($core.List<$core.int> value) => $_setBytes(0, value);
  @$pb.TagNumber(1)
  $core.bool hasPayload() => $_has(0);
  @$pb.TagNumber(1)
  void clearPayload() => $_clearField(1);
}

class SqrtRequest extends $pb.GeneratedMessage {
  factory SqrtRequest({
    $core.double? value,
  }) {
    final result = SqrtRequest._();
    if (value != null) result.value = value;
    return result;
  }

  SqrtRequest._();

  factory SqrtRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SqrtRequest()..mergeFromBuffer(data, registry);
  factory SqrtRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SqrtRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SqrtRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: SqrtRequest.$_createMessage)
    ..aD(1, _omitFieldNames ? '' : 'value')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SqrtRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SqrtRequest copyWith(void Function(SqrtRequest) updates) =>
      super.copyWith((message) => updates(message as SqrtRequest))
          as SqrtRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use SqrtRequest() / SqrtRequest.new instead')
  static SqrtRequest create() => SqrtRequest._();
  static $pb.GeneratedMessage $_createMessage() => SqrtRequest._();
  @$core.override
  SqrtRequest createEmptyInstance() => SqrtRequest._();
  @$core.pragma('dart2js:noInline')
  static SqrtRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<SqrtRequest>(
          SqrtRequest.$_createMessage);
  static SqrtRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.double get value => $_getN(0);
  @$pb.TagNumber(1)
  set value($core.double value) => $_setDouble(0, value);
  @$pb.TagNumber(1)
  $core.bool hasValue() => $_has(0);
  @$pb.TagNumber(1)
  void clearValue() => $_clearField(1);
}

class SqrtResponse extends $pb.GeneratedMessage {
  factory SqrtResponse({
    $core.double? value,
  }) {
    final result = SqrtResponse._();
    if (value != null) result.value = value;
    return result;
  }

  SqrtResponse._();

  factory SqrtResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SqrtResponse()..mergeFromBuffer(data, registry);
  factory SqrtResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SqrtResponse()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SqrtResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: SqrtResponse.$_createMessage)
    ..aD(1, _omitFieldNames ? '' : 'value')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SqrtResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SqrtResponse copyWith(void Function(SqrtResponse) updates) =>
      super.copyWith((message) => updates(message as SqrtResponse))
          as SqrtResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use SqrtResponse() / SqrtResponse.new instead')
  static SqrtResponse create() => SqrtResponse._();
  static $pb.GeneratedMessage $_createMessage() => SqrtResponse._();
  @$core.override
  SqrtResponse createEmptyInstance() => SqrtResponse._();
  @$core.pragma('dart2js:noInline')
  static SqrtResponse getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<SqrtResponse>(
          SqrtResponse.$_createMessage);
  static SqrtResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.double get value => $_getN(0);
  @$pb.TagNumber(1)
  set value($core.double value) => $_setDouble(0, value);
  @$pb.TagNumber(1)
  $core.bool hasValue() => $_has(0);
  @$pb.TagNumber(1)
  void clearValue() => $_clearField(1);
}

class FibRequest extends $pb.GeneratedMessage {
  factory FibRequest({
    $core.int? n,
  }) {
    final result = FibRequest._();
    if (n != null) result.n = n;
    return result;
  }

  FibRequest._();

  factory FibRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      FibRequest()..mergeFromBuffer(data, registry);
  factory FibRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      FibRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'FibRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: FibRequest.$_createMessage)
    ..aI(1, _omitFieldNames ? '' : 'n', fieldType: $pb.PbFieldType.OU3)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FibRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FibRequest copyWith(void Function(FibRequest) updates) =>
      super.copyWith((message) => updates(message as FibRequest)) as FibRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use FibRequest() / FibRequest.new instead')
  static FibRequest create() => FibRequest._();
  static $pb.GeneratedMessage $_createMessage() => FibRequest._();
  @$core.override
  FibRequest createEmptyInstance() => FibRequest._();
  @$core.pragma('dart2js:noInline')
  static FibRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FibRequest>(FibRequest.$_createMessage);
  static FibRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.int get n => $_getIZ(0);
  @$pb.TagNumber(1)
  set n($core.int value) => $_setUnsignedInt32(0, value);
  @$pb.TagNumber(1)
  $core.bool hasN() => $_has(0);
  @$pb.TagNumber(1)
  void clearN() => $_clearField(1);
}

class FibResponse extends $pb.GeneratedMessage {
  factory FibResponse({
    $fixnum.Int64? value,
  }) {
    final result = FibResponse._();
    if (value != null) result.value = value;
    return result;
  }

  FibResponse._();

  factory FibResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      FibResponse()..mergeFromBuffer(data, registry);
  factory FibResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      FibResponse()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'FibResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: FibResponse.$_createMessage)
    ..a<$fixnum.Int64>(1, _omitFieldNames ? '' : 'value', $pb.PbFieldType.OU6,
        defaultOrMaker: $fixnum.Int64.ZERO)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FibResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FibResponse copyWith(void Function(FibResponse) updates) =>
      super.copyWith((message) => updates(message as FibResponse))
          as FibResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use FibResponse() / FibResponse.new instead')
  static FibResponse create() => FibResponse._();
  static $pb.GeneratedMessage $_createMessage() => FibResponse._();
  @$core.override
  FibResponse createEmptyInstance() => FibResponse._();
  @$core.pragma('dart2js:noInline')
  static FibResponse getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<FibResponse>(
          FibResponse.$_createMessage);
  static FibResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $fixnum.Int64 get value => $_getI64(0);
  @$pb.TagNumber(1)
  set value($fixnum.Int64 value) => $_setInt64(0, value);
  @$pb.TagNumber(1)
  $core.bool hasValue() => $_has(0);
  @$pb.TagNumber(1)
  void clearValue() => $_clearField(1);
}

class GetTimeRequest extends $pb.GeneratedMessage {
  factory GetTimeRequest() => GetTimeRequest._();

  GetTimeRequest._();

  factory GetTimeRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      GetTimeRequest()..mergeFromBuffer(data, registry);
  factory GetTimeRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      GetTimeRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'GetTimeRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: GetTimeRequest.$_createMessage)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetTimeRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetTimeRequest copyWith(void Function(GetTimeRequest) updates) =>
      super.copyWith((message) => updates(message as GetTimeRequest))
          as GetTimeRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use GetTimeRequest() / GetTimeRequest.new instead')
  static GetTimeRequest create() => GetTimeRequest._();
  static $pb.GeneratedMessage $_createMessage() => GetTimeRequest._();
  @$core.override
  GetTimeRequest createEmptyInstance() => GetTimeRequest._();
  @$core.pragma('dart2js:noInline')
  static GetTimeRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<GetTimeRequest>(
          GetTimeRequest.$_createMessage);
  static GetTimeRequest? _defaultInstance;
}

class SetTimeRequest extends $pb.GeneratedMessage {
  factory SetTimeRequest({
    $fixnum.Int64? unixMillis,
  }) {
    final result = SetTimeRequest._();
    if (unixMillis != null) result.unixMillis = unixMillis;
    return result;
  }

  SetTimeRequest._();

  factory SetTimeRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SetTimeRequest()..mergeFromBuffer(data, registry);
  factory SetTimeRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SetTimeRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SetTimeRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: SetTimeRequest.$_createMessage)
    ..aInt64(1, _omitFieldNames ? '' : 'unixMillis')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SetTimeRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SetTimeRequest copyWith(void Function(SetTimeRequest) updates) =>
      super.copyWith((message) => updates(message as SetTimeRequest))
          as SetTimeRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use SetTimeRequest() / SetTimeRequest.new instead')
  static SetTimeRequest create() => SetTimeRequest._();
  static $pb.GeneratedMessage $_createMessage() => SetTimeRequest._();
  @$core.override
  SetTimeRequest createEmptyInstance() => SetTimeRequest._();
  @$core.pragma('dart2js:noInline')
  static SetTimeRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<SetTimeRequest>(
          SetTimeRequest.$_createMessage);
  static SetTimeRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $fixnum.Int64 get unixMillis => $_getI64(0);
  @$pb.TagNumber(1)
  set unixMillis($fixnum.Int64 value) => $_setInt64(0, value);
  @$pb.TagNumber(1)
  $core.bool hasUnixMillis() => $_has(0);
  @$pb.TagNumber(1)
  void clearUnixMillis() => $_clearField(1);
}

class TimeResponse extends $pb.GeneratedMessage {
  factory TimeResponse({
    $fixnum.Int64? unixMillis,
  }) {
    final result = TimeResponse._();
    if (unixMillis != null) result.unixMillis = unixMillis;
    return result;
  }

  TimeResponse._();

  factory TimeResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      TimeResponse()..mergeFromBuffer(data, registry);
  factory TimeResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      TimeResponse()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'TimeResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: TimeResponse.$_createMessage)
    ..aInt64(1, _omitFieldNames ? '' : 'unixMillis')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  TimeResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  TimeResponse copyWith(void Function(TimeResponse) updates) =>
      super.copyWith((message) => updates(message as TimeResponse))
          as TimeResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use TimeResponse() / TimeResponse.new instead')
  static TimeResponse create() => TimeResponse._();
  static $pb.GeneratedMessage $_createMessage() => TimeResponse._();
  @$core.override
  TimeResponse createEmptyInstance() => TimeResponse._();
  @$core.pragma('dart2js:noInline')
  static TimeResponse getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<TimeResponse>(
          TimeResponse.$_createMessage);
  static TimeResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $fixnum.Int64 get unixMillis => $_getI64(0);
  @$pb.TagNumber(1)
  set unixMillis($fixnum.Int64 value) => $_setInt64(0, value);
  @$pb.TagNumber(1)
  $core.bool hasUnixMillis() => $_has(0);
  @$pb.TagNumber(1)
  void clearUnixMillis() => $_clearField(1);
}

/// Sets the serial number NNNN in the advertised "Verdigris NNNN" name
/// (persisted on-device across power cycles).
class SetSerialNumberRequest extends $pb.GeneratedMessage {
  factory SetSerialNumberRequest({
    $core.int? serialNumber,
  }) {
    final result = SetSerialNumberRequest._();
    if (serialNumber != null) result.serialNumber = serialNumber;
    return result;
  }

  SetSerialNumberRequest._();

  factory SetSerialNumberRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SetSerialNumberRequest()..mergeFromBuffer(data, registry);
  factory SetSerialNumberRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SetSerialNumberRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SetSerialNumberRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: SetSerialNumberRequest.$_createMessage)
    ..aI(1, _omitFieldNames ? '' : 'serialNumber',
        fieldType: $pb.PbFieldType.OU3)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SetSerialNumberRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SetSerialNumberRequest copyWith(
          void Function(SetSerialNumberRequest) updates) =>
      super.copyWith((message) => updates(message as SetSerialNumberRequest))
          as SetSerialNumberRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated(
      'Use SetSerialNumberRequest() / SetSerialNumberRequest.new instead')
  static SetSerialNumberRequest create() => SetSerialNumberRequest._();
  static $pb.GeneratedMessage $_createMessage() => SetSerialNumberRequest._();
  @$core.override
  SetSerialNumberRequest createEmptyInstance() => SetSerialNumberRequest._();
  @$core.pragma('dart2js:noInline')
  static SetSerialNumberRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SetSerialNumberRequest>(
          SetSerialNumberRequest.$_createMessage);
  static SetSerialNumberRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.int get serialNumber => $_getIZ(0);
  @$pb.TagNumber(1)
  set serialNumber($core.int value) => $_setUnsignedInt32(0, value);
  @$pb.TagNumber(1)
  $core.bool hasSerialNumber() => $_has(0);
  @$pb.TagNumber(1)
  void clearSerialNumber() => $_clearField(1);
}

class GetSerialNumberRequest extends $pb.GeneratedMessage {
  factory GetSerialNumberRequest() => GetSerialNumberRequest._();

  GetSerialNumberRequest._();

  factory GetSerialNumberRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      GetSerialNumberRequest()..mergeFromBuffer(data, registry);
  factory GetSerialNumberRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      GetSerialNumberRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'GetSerialNumberRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: GetSerialNumberRequest.$_createMessage)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetSerialNumberRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetSerialNumberRequest copyWith(
          void Function(GetSerialNumberRequest) updates) =>
      super.copyWith((message) => updates(message as GetSerialNumberRequest))
          as GetSerialNumberRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated(
      'Use GetSerialNumberRequest() / GetSerialNumberRequest.new instead')
  static GetSerialNumberRequest create() => GetSerialNumberRequest._();
  static $pb.GeneratedMessage $_createMessage() => GetSerialNumberRequest._();
  @$core.override
  GetSerialNumberRequest createEmptyInstance() => GetSerialNumberRequest._();
  @$core.pragma('dart2js:noInline')
  static GetSerialNumberRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetSerialNumberRequest>(
          GetSerialNumberRequest.$_createMessage);
  static GetSerialNumberRequest? _defaultInstance;
}

class NameResponse extends $pb.GeneratedMessage {
  factory NameResponse({
    $core.String? name,
  }) {
    final result = NameResponse._();
    if (name != null) result.name = name;
    return result;
  }

  NameResponse._();

  factory NameResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      NameResponse()..mergeFromBuffer(data, registry);
  factory NameResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      NameResponse()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'NameResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: NameResponse.$_createMessage)
    ..aOS(1, _omitFieldNames ? '' : 'name')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NameResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NameResponse copyWith(void Function(NameResponse) updates) =>
      super.copyWith((message) => updates(message as NameResponse))
          as NameResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use NameResponse() / NameResponse.new instead')
  static NameResponse create() => NameResponse._();
  static $pb.GeneratedMessage $_createMessage() => NameResponse._();
  @$core.override
  NameResponse createEmptyInstance() => NameResponse._();
  @$core.pragma('dart2js:noInline')
  static NameResponse getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<NameResponse>(
          NameResponse.$_createMessage);
  static NameResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get name => $_getSZ(0);
  @$pb.TagNumber(1)
  set name($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasName() => $_has(0);
  @$pb.TagNumber(1)
  void clearName() => $_clearField(1);
}

/// LED 1 on the DK is the app-controlled indicator: Get is low privilege,
/// Set is high. (Button 1 still blinks it as a heartbeat while held; release
/// restores the app-set state.)
class GetLedRequest extends $pb.GeneratedMessage {
  factory GetLedRequest() => GetLedRequest._();

  GetLedRequest._();

  factory GetLedRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      GetLedRequest()..mergeFromBuffer(data, registry);
  factory GetLedRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      GetLedRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'GetLedRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: GetLedRequest.$_createMessage)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetLedRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetLedRequest copyWith(void Function(GetLedRequest) updates) =>
      super.copyWith((message) => updates(message as GetLedRequest))
          as GetLedRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use GetLedRequest() / GetLedRequest.new instead')
  static GetLedRequest create() => GetLedRequest._();
  static $pb.GeneratedMessage $_createMessage() => GetLedRequest._();
  @$core.override
  GetLedRequest createEmptyInstance() => GetLedRequest._();
  @$core.pragma('dart2js:noInline')
  static GetLedRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<GetLedRequest>(
          GetLedRequest.$_createMessage);
  static GetLedRequest? _defaultInstance;
}

class SetLedRequest extends $pb.GeneratedMessage {
  factory SetLedRequest({
    $core.bool? on,
  }) {
    final result = SetLedRequest._();
    if (on != null) result.on = on;
    return result;
  }

  SetLedRequest._();

  factory SetLedRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SetLedRequest()..mergeFromBuffer(data, registry);
  factory SetLedRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      SetLedRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SetLedRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: SetLedRequest.$_createMessage)
    ..aOB(1, _omitFieldNames ? '' : 'on')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SetLedRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SetLedRequest copyWith(void Function(SetLedRequest) updates) =>
      super.copyWith((message) => updates(message as SetLedRequest))
          as SetLedRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use SetLedRequest() / SetLedRequest.new instead')
  static SetLedRequest create() => SetLedRequest._();
  static $pb.GeneratedMessage $_createMessage() => SetLedRequest._();
  @$core.override
  SetLedRequest createEmptyInstance() => SetLedRequest._();
  @$core.pragma('dart2js:noInline')
  static SetLedRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<SetLedRequest>(
          SetLedRequest.$_createMessage);
  static SetLedRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.bool get on => $_getBF(0);
  @$pb.TagNumber(1)
  set on($core.bool value) => $_setBool(0, value);
  @$pb.TagNumber(1)
  $core.bool hasOn() => $_has(0);
  @$pb.TagNumber(1)
  void clearOn() => $_clearField(1);
}

class LedResponse extends $pb.GeneratedMessage {
  factory LedResponse({
    $core.bool? on,
  }) {
    final result = LedResponse._();
    if (on != null) result.on = on;
    return result;
  }

  LedResponse._();

  factory LedResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      LedResponse()..mergeFromBuffer(data, registry);
  factory LedResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      LedResponse()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'LedResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: LedResponse.$_createMessage)
    ..aOB(1, _omitFieldNames ? '' : 'on')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  LedResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  LedResponse copyWith(void Function(LedResponse) updates) =>
      super.copyWith((message) => updates(message as LedResponse))
          as LedResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use LedResponse() / LedResponse.new instead')
  static LedResponse create() => LedResponse._();
  static $pb.GeneratedMessage $_createMessage() => LedResponse._();
  @$core.override
  LedResponse createEmptyInstance() => LedResponse._();
  @$core.pragma('dart2js:noInline')
  static LedResponse getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<LedResponse>(
          LedResponse.$_createMessage);
  static LedResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.bool get on => $_getBF(0);
  @$pb.TagNumber(1)
  set on($core.bool value) => $_setBool(0, value);
  @$pb.TagNumber(1)
  $core.bool hasOn() => $_has(0);
  @$pb.TagNumber(1)
  void clearOn() => $_clearField(1);
}

/// Pairing ceremony (only meaningful inside a secure session): the device arms
/// a confirmation window and succeeds once its button is pressed, pinning the
/// session's client key into the requested privilege slot (one pinned client
/// per tier). Unpair clears the caller's own slot.
class PairRequest extends $pb.GeneratedMessage {
  factory PairRequest({
    Privilege? privilege,
  }) {
    final result = PairRequest._();
    if (privilege != null) result.privilege = privilege;
    return result;
  }

  PairRequest._();

  factory PairRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      PairRequest()..mergeFromBuffer(data, registry);
  factory PairRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      PairRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'PairRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: PairRequest.$_createMessage)
    ..aE<Privilege>(1, _omitFieldNames ? '' : 'privilege',
        enumValues: Privilege.values)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  PairRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  PairRequest copyWith(void Function(PairRequest) updates) =>
      super.copyWith((message) => updates(message as PairRequest))
          as PairRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use PairRequest() / PairRequest.new instead')
  static PairRequest create() => PairRequest._();
  static $pb.GeneratedMessage $_createMessage() => PairRequest._();
  @$core.override
  PairRequest createEmptyInstance() => PairRequest._();
  @$core.pragma('dart2js:noInline')
  static PairRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<PairRequest>(
          PairRequest.$_createMessage);
  static PairRequest? _defaultInstance;

  @$pb.TagNumber(1)
  Privilege get privilege => $_getN(0);
  @$pb.TagNumber(1)
  set privilege(Privilege value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasPrivilege() => $_has(0);
  @$pb.TagNumber(1)
  void clearPrivilege() => $_clearField(1);
}

class UnpairRequest extends $pb.GeneratedMessage {
  factory UnpairRequest() => UnpairRequest._();

  UnpairRequest._();

  factory UnpairRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      UnpairRequest()..mergeFromBuffer(data, registry);
  factory UnpairRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      UnpairRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'UnpairRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: UnpairRequest.$_createMessage)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UnpairRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UnpairRequest copyWith(void Function(UnpairRequest) updates) =>
      super.copyWith((message) => updates(message as UnpairRequest))
          as UnpairRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use UnpairRequest() / UnpairRequest.new instead')
  static UnpairRequest create() => UnpairRequest._();
  static $pb.GeneratedMessage $_createMessage() => UnpairRequest._();
  @$core.override
  UnpairRequest createEmptyInstance() => UnpairRequest._();
  @$core.pragma('dart2js:noInline')
  static UnpairRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<UnpairRequest>(
          UnpairRequest.$_createMessage);
  static UnpairRequest? _defaultInstance;
}

class AckResponse extends $pb.GeneratedMessage {
  factory AckResponse() => AckResponse._();

  AckResponse._();

  factory AckResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      AckResponse()..mergeFromBuffer(data, registry);
  factory AckResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      AckResponse()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'AckResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: AckResponse.$_createMessage)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AckResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AckResponse copyWith(void Function(AckResponse) updates) =>
      super.copyWith((message) => updates(message as AckResponse))
          as AckResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use AckResponse() / AckResponse.new instead')
  static AckResponse create() => AckResponse._();
  static $pb.GeneratedMessage $_createMessage() => AckResponse._();
  @$core.override
  AckResponse createEmptyInstance() => AckResponse._();
  @$core.pragma('dart2js:noInline')
  static AckResponse getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<AckResponse>(
          AckResponse.$_createMessage);
  static AckResponse? _defaultInstance;
}

/// Enrollment (only inside a session opened with the device's factory
/// enrollment key, which may send nothing else): pins the caller-supplied
/// client public key into the low-privilege slot, replacing any earlier
/// occupant, so exactly one enrolled client holds that slot at a time.
class EnrollRequest extends $pb.GeneratedMessage {
  factory EnrollRequest({
    $core.List<$core.int>? clientPublicKey,
  }) {
    final result = EnrollRequest._();
    if (clientPublicKey != null) result.clientPublicKey = clientPublicKey;
    return result;
  }

  EnrollRequest._();

  factory EnrollRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      EnrollRequest()..mergeFromBuffer(data, registry);
  factory EnrollRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      EnrollRequest()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'EnrollRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: EnrollRequest.$_createMessage)
    ..a<$core.List<$core.int>>(
        1, _omitFieldNames ? '' : 'clientPublicKey', $pb.PbFieldType.OY)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  EnrollRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  EnrollRequest copyWith(void Function(EnrollRequest) updates) =>
      super.copyWith((message) => updates(message as EnrollRequest))
          as EnrollRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use EnrollRequest() / EnrollRequest.new instead')
  static EnrollRequest create() => EnrollRequest._();
  static $pb.GeneratedMessage $_createMessage() => EnrollRequest._();
  @$core.override
  EnrollRequest createEmptyInstance() => EnrollRequest._();
  @$core.pragma('dart2js:noInline')
  static EnrollRequest getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<EnrollRequest>(
          EnrollRequest.$_createMessage);
  static EnrollRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.List<$core.int> get clientPublicKey => $_getN(0);
  @$pb.TagNumber(1)
  set clientPublicKey($core.List<$core.int> value) => $_setBytes(0, value);
  @$pb.TagNumber(1)
  $core.bool hasClientPublicKey() => $_has(0);
  @$pb.TagNumber(1)
  void clearClientPublicKey() => $_clearField(1);
}

enum Request_Op {
  echo,
  sqrt,
  fib,
  getTime,
  setTime,
  setSerialNumber,
  pair,
  unpair,
  getSerialNumber,
  getLed,
  setLed,
  enroll,
  notSet
}

class Request extends $pb.GeneratedMessage {
  factory Request({
    $core.int? id,
    EchoRequest? echo,
    SqrtRequest? sqrt,
    FibRequest? fib,
    GetTimeRequest? getTime,
    SetTimeRequest? setTime,
    SetSerialNumberRequest? setSerialNumber,
    PairRequest? pair,
    UnpairRequest? unpair,
    GetSerialNumberRequest? getSerialNumber,
    GetLedRequest? getLed,
    SetLedRequest? setLed,
    EnrollRequest? enroll,
  }) {
    final result = Request._();
    if (id != null) result.id = id;
    if (echo != null) result.echo = echo;
    if (sqrt != null) result.sqrt = sqrt;
    if (fib != null) result.fib = fib;
    if (getTime != null) result.getTime = getTime;
    if (setTime != null) result.setTime = setTime;
    if (setSerialNumber != null) result.setSerialNumber = setSerialNumber;
    if (pair != null) result.pair = pair;
    if (unpair != null) result.unpair = unpair;
    if (getSerialNumber != null) result.getSerialNumber = getSerialNumber;
    if (getLed != null) result.getLed = getLed;
    if (setLed != null) result.setLed = setLed;
    if (enroll != null) result.enroll = enroll;
    return result;
  }

  Request._();

  factory Request.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      Request()..mergeFromBuffer(data, registry);
  factory Request.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      Request()..mergeFromJson(json, registry);

  static const $core.Map<$core.int, Request_Op> _Request_OpByTag = {
    2: Request_Op.echo,
    3: Request_Op.sqrt,
    4: Request_Op.fib,
    5: Request_Op.getTime,
    6: Request_Op.setTime,
    7: Request_Op.setSerialNumber,
    8: Request_Op.pair,
    9: Request_Op.unpair,
    10: Request_Op.getSerialNumber,
    11: Request_Op.getLed,
    12: Request_Op.setLed,
    13: Request_Op.enroll,
    0: Request_Op.notSet
  };
  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'Request',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: Request.$_createMessage)
    ..oo(0, [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13])
    ..aI(1, _omitFieldNames ? '' : 'id', fieldType: $pb.PbFieldType.OU3)
    ..aOM<EchoRequest>(2, _omitFieldNames ? '' : 'echo',
        subBuilder: EchoRequest.$_createMessage)
    ..aOM<SqrtRequest>(3, _omitFieldNames ? '' : 'sqrt',
        subBuilder: SqrtRequest.$_createMessage)
    ..aOM<FibRequest>(4, _omitFieldNames ? '' : 'fib',
        subBuilder: FibRequest.$_createMessage)
    ..aOM<GetTimeRequest>(5, _omitFieldNames ? '' : 'getTime',
        subBuilder: GetTimeRequest.$_createMessage)
    ..aOM<SetTimeRequest>(6, _omitFieldNames ? '' : 'setTime',
        subBuilder: SetTimeRequest.$_createMessage)
    ..aOM<SetSerialNumberRequest>(7, _omitFieldNames ? '' : 'setSerialNumber',
        subBuilder: SetSerialNumberRequest.$_createMessage)
    ..aOM<PairRequest>(8, _omitFieldNames ? '' : 'pair',
        subBuilder: PairRequest.$_createMessage)
    ..aOM<UnpairRequest>(9, _omitFieldNames ? '' : 'unpair',
        subBuilder: UnpairRequest.$_createMessage)
    ..aOM<GetSerialNumberRequest>(10, _omitFieldNames ? '' : 'getSerialNumber',
        subBuilder: GetSerialNumberRequest.$_createMessage)
    ..aOM<GetLedRequest>(11, _omitFieldNames ? '' : 'getLed',
        subBuilder: GetLedRequest.$_createMessage)
    ..aOM<SetLedRequest>(12, _omitFieldNames ? '' : 'setLed',
        subBuilder: SetLedRequest.$_createMessage)
    ..aOM<EnrollRequest>(13, _omitFieldNames ? '' : 'enroll',
        subBuilder: EnrollRequest.$_createMessage)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  Request clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  Request copyWith(void Function(Request) updates) =>
      super.copyWith((message) => updates(message as Request)) as Request;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use Request() / Request.new instead')
  static Request create() => Request._();
  static $pb.GeneratedMessage $_createMessage() => Request._();
  @$core.override
  Request createEmptyInstance() => Request._();
  @$core.pragma('dart2js:noInline')
  static Request getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<Request>(Request.$_createMessage);
  static Request? _defaultInstance;

  @$pb.TagNumber(2)
  @$pb.TagNumber(3)
  @$pb.TagNumber(4)
  @$pb.TagNumber(5)
  @$pb.TagNumber(6)
  @$pb.TagNumber(7)
  @$pb.TagNumber(8)
  @$pb.TagNumber(9)
  @$pb.TagNumber(10)
  @$pb.TagNumber(11)
  @$pb.TagNumber(12)
  @$pb.TagNumber(13)
  Request_Op whichOp() => _Request_OpByTag[$_whichOneof(0)]!;
  @$pb.TagNumber(2)
  @$pb.TagNumber(3)
  @$pb.TagNumber(4)
  @$pb.TagNumber(5)
  @$pb.TagNumber(6)
  @$pb.TagNumber(7)
  @$pb.TagNumber(8)
  @$pb.TagNumber(9)
  @$pb.TagNumber(10)
  @$pb.TagNumber(11)
  @$pb.TagNumber(12)
  @$pb.TagNumber(13)
  void clearOp() => $_clearField($_whichOneof(0));

  @$pb.TagNumber(1)
  $core.int get id => $_getIZ(0);
  @$pb.TagNumber(1)
  set id($core.int value) => $_setUnsignedInt32(0, value);
  @$pb.TagNumber(1)
  $core.bool hasId() => $_has(0);
  @$pb.TagNumber(1)
  void clearId() => $_clearField(1);

  @$pb.TagNumber(2)
  EchoRequest get echo => $_getN(1);
  @$pb.TagNumber(2)
  set echo(EchoRequest value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasEcho() => $_has(1);
  @$pb.TagNumber(2)
  void clearEcho() => $_clearField(2);
  @$pb.TagNumber(2)
  EchoRequest ensureEcho() => $_ensure(1);

  @$pb.TagNumber(3)
  SqrtRequest get sqrt => $_getN(2);
  @$pb.TagNumber(3)
  set sqrt(SqrtRequest value) => $_setField(3, value);
  @$pb.TagNumber(3)
  $core.bool hasSqrt() => $_has(2);
  @$pb.TagNumber(3)
  void clearSqrt() => $_clearField(3);
  @$pb.TagNumber(3)
  SqrtRequest ensureSqrt() => $_ensure(2);

  @$pb.TagNumber(4)
  FibRequest get fib => $_getN(3);
  @$pb.TagNumber(4)
  set fib(FibRequest value) => $_setField(4, value);
  @$pb.TagNumber(4)
  $core.bool hasFib() => $_has(3);
  @$pb.TagNumber(4)
  void clearFib() => $_clearField(4);
  @$pb.TagNumber(4)
  FibRequest ensureFib() => $_ensure(3);

  @$pb.TagNumber(5)
  GetTimeRequest get getTime => $_getN(4);
  @$pb.TagNumber(5)
  set getTime(GetTimeRequest value) => $_setField(5, value);
  @$pb.TagNumber(5)
  $core.bool hasGetTime() => $_has(4);
  @$pb.TagNumber(5)
  void clearGetTime() => $_clearField(5);
  @$pb.TagNumber(5)
  GetTimeRequest ensureGetTime() => $_ensure(4);

  @$pb.TagNumber(6)
  SetTimeRequest get setTime => $_getN(5);
  @$pb.TagNumber(6)
  set setTime(SetTimeRequest value) => $_setField(6, value);
  @$pb.TagNumber(6)
  $core.bool hasSetTime() => $_has(5);
  @$pb.TagNumber(6)
  void clearSetTime() => $_clearField(6);
  @$pb.TagNumber(6)
  SetTimeRequest ensureSetTime() => $_ensure(5);

  @$pb.TagNumber(7)
  SetSerialNumberRequest get setSerialNumber => $_getN(6);
  @$pb.TagNumber(7)
  set setSerialNumber(SetSerialNumberRequest value) => $_setField(7, value);
  @$pb.TagNumber(7)
  $core.bool hasSetSerialNumber() => $_has(6);
  @$pb.TagNumber(7)
  void clearSetSerialNumber() => $_clearField(7);
  @$pb.TagNumber(7)
  SetSerialNumberRequest ensureSetSerialNumber() => $_ensure(6);

  @$pb.TagNumber(8)
  PairRequest get pair => $_getN(7);
  @$pb.TagNumber(8)
  set pair(PairRequest value) => $_setField(8, value);
  @$pb.TagNumber(8)
  $core.bool hasPair() => $_has(7);
  @$pb.TagNumber(8)
  void clearPair() => $_clearField(8);
  @$pb.TagNumber(8)
  PairRequest ensurePair() => $_ensure(7);

  @$pb.TagNumber(9)
  UnpairRequest get unpair => $_getN(8);
  @$pb.TagNumber(9)
  set unpair(UnpairRequest value) => $_setField(9, value);
  @$pb.TagNumber(9)
  $core.bool hasUnpair() => $_has(8);
  @$pb.TagNumber(9)
  void clearUnpair() => $_clearField(9);
  @$pb.TagNumber(9)
  UnpairRequest ensureUnpair() => $_ensure(8);

  @$pb.TagNumber(10)
  GetSerialNumberRequest get getSerialNumber => $_getN(9);
  @$pb.TagNumber(10)
  set getSerialNumber(GetSerialNumberRequest value) => $_setField(10, value);
  @$pb.TagNumber(10)
  $core.bool hasGetSerialNumber() => $_has(9);
  @$pb.TagNumber(10)
  void clearGetSerialNumber() => $_clearField(10);
  @$pb.TagNumber(10)
  GetSerialNumberRequest ensureGetSerialNumber() => $_ensure(9);

  @$pb.TagNumber(11)
  GetLedRequest get getLed => $_getN(10);
  @$pb.TagNumber(11)
  set getLed(GetLedRequest value) => $_setField(11, value);
  @$pb.TagNumber(11)
  $core.bool hasGetLed() => $_has(10);
  @$pb.TagNumber(11)
  void clearGetLed() => $_clearField(11);
  @$pb.TagNumber(11)
  GetLedRequest ensureGetLed() => $_ensure(10);

  @$pb.TagNumber(12)
  SetLedRequest get setLed => $_getN(11);
  @$pb.TagNumber(12)
  set setLed(SetLedRequest value) => $_setField(12, value);
  @$pb.TagNumber(12)
  $core.bool hasSetLed() => $_has(11);
  @$pb.TagNumber(12)
  void clearSetLed() => $_clearField(12);
  @$pb.TagNumber(12)
  SetLedRequest ensureSetLed() => $_ensure(11);

  @$pb.TagNumber(13)
  EnrollRequest get enroll => $_getN(12);
  @$pb.TagNumber(13)
  set enroll(EnrollRequest value) => $_setField(13, value);
  @$pb.TagNumber(13)
  $core.bool hasEnroll() => $_has(12);
  @$pb.TagNumber(13)
  void clearEnroll() => $_clearField(13);
  @$pb.TagNumber(13)
  EnrollRequest ensureEnroll() => $_ensure(12);
}

enum Response_Result { echo, sqrt, fib, time, name, ack, led, notSet }

class Response extends $pb.GeneratedMessage {
  factory Response({
    $core.int? id,
    Status? status,
    $core.String? detail,
    EchoResponse? echo,
    SqrtResponse? sqrt,
    FibResponse? fib,
    TimeResponse? time,
    NameResponse? name,
    AckResponse? ack,
    LedResponse? led,
  }) {
    final result = Response._();
    if (id != null) result.id = id;
    if (status != null) result.status = status;
    if (detail != null) result.detail = detail;
    if (echo != null) result.echo = echo;
    if (sqrt != null) result.sqrt = sqrt;
    if (fib != null) result.fib = fib;
    if (time != null) result.time = time;
    if (name != null) result.name = name;
    if (ack != null) result.ack = ack;
    if (led != null) result.led = led;
    return result;
  }

  Response._();

  factory Response.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      Response()..mergeFromBuffer(data, registry);
  factory Response.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      Response()..mergeFromJson(json, registry);

  static const $core.Map<$core.int, Response_Result> _Response_ResultByTag = {
    4: Response_Result.echo,
    5: Response_Result.sqrt,
    6: Response_Result.fib,
    7: Response_Result.time,
    8: Response_Result.name,
    9: Response_Result.ack,
    10: Response_Result.led,
    0: Response_Result.notSet
  };
  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'Response',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'verdigris'),
      createEmptyInstance: Response.$_createMessage)
    ..oo(0, [4, 5, 6, 7, 8, 9, 10])
    ..aI(1, _omitFieldNames ? '' : 'id', fieldType: $pb.PbFieldType.OU3)
    ..aE<Status>(2, _omitFieldNames ? '' : 'status', enumValues: Status.values)
    ..aOS(3, _omitFieldNames ? '' : 'detail')
    ..aOM<EchoResponse>(4, _omitFieldNames ? '' : 'echo',
        subBuilder: EchoResponse.$_createMessage)
    ..aOM<SqrtResponse>(5, _omitFieldNames ? '' : 'sqrt',
        subBuilder: SqrtResponse.$_createMessage)
    ..aOM<FibResponse>(6, _omitFieldNames ? '' : 'fib',
        subBuilder: FibResponse.$_createMessage)
    ..aOM<TimeResponse>(7, _omitFieldNames ? '' : 'time',
        subBuilder: TimeResponse.$_createMessage)
    ..aOM<NameResponse>(8, _omitFieldNames ? '' : 'name',
        subBuilder: NameResponse.$_createMessage)
    ..aOM<AckResponse>(9, _omitFieldNames ? '' : 'ack',
        subBuilder: AckResponse.$_createMessage)
    ..aOM<LedResponse>(10, _omitFieldNames ? '' : 'led',
        subBuilder: LedResponse.$_createMessage)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  Response clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  Response copyWith(void Function(Response) updates) =>
      super.copyWith((message) => updates(message as Response)) as Response;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  @$core.Deprecated('Use Response() / Response.new instead')
  static Response create() => Response._();
  static $pb.GeneratedMessage $_createMessage() => Response._();
  @$core.override
  Response createEmptyInstance() => Response._();
  @$core.pragma('dart2js:noInline')
  static Response getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<Response>(Response.$_createMessage);
  static Response? _defaultInstance;

  @$pb.TagNumber(4)
  @$pb.TagNumber(5)
  @$pb.TagNumber(6)
  @$pb.TagNumber(7)
  @$pb.TagNumber(8)
  @$pb.TagNumber(9)
  @$pb.TagNumber(10)
  Response_Result whichResult() => _Response_ResultByTag[$_whichOneof(0)]!;
  @$pb.TagNumber(4)
  @$pb.TagNumber(5)
  @$pb.TagNumber(6)
  @$pb.TagNumber(7)
  @$pb.TagNumber(8)
  @$pb.TagNumber(9)
  @$pb.TagNumber(10)
  void clearResult() => $_clearField($_whichOneof(0));

  @$pb.TagNumber(1)
  $core.int get id => $_getIZ(0);
  @$pb.TagNumber(1)
  set id($core.int value) => $_setUnsignedInt32(0, value);
  @$pb.TagNumber(1)
  $core.bool hasId() => $_has(0);
  @$pb.TagNumber(1)
  void clearId() => $_clearField(1);

  @$pb.TagNumber(2)
  Status get status => $_getN(1);
  @$pb.TagNumber(2)
  set status(Status value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasStatus() => $_has(1);
  @$pb.TagNumber(2)
  void clearStatus() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get detail => $_getSZ(2);
  @$pb.TagNumber(3)
  set detail($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasDetail() => $_has(2);
  @$pb.TagNumber(3)
  void clearDetail() => $_clearField(3);

  @$pb.TagNumber(4)
  EchoResponse get echo => $_getN(3);
  @$pb.TagNumber(4)
  set echo(EchoResponse value) => $_setField(4, value);
  @$pb.TagNumber(4)
  $core.bool hasEcho() => $_has(3);
  @$pb.TagNumber(4)
  void clearEcho() => $_clearField(4);
  @$pb.TagNumber(4)
  EchoResponse ensureEcho() => $_ensure(3);

  @$pb.TagNumber(5)
  SqrtResponse get sqrt => $_getN(4);
  @$pb.TagNumber(5)
  set sqrt(SqrtResponse value) => $_setField(5, value);
  @$pb.TagNumber(5)
  $core.bool hasSqrt() => $_has(4);
  @$pb.TagNumber(5)
  void clearSqrt() => $_clearField(5);
  @$pb.TagNumber(5)
  SqrtResponse ensureSqrt() => $_ensure(4);

  @$pb.TagNumber(6)
  FibResponse get fib => $_getN(5);
  @$pb.TagNumber(6)
  set fib(FibResponse value) => $_setField(6, value);
  @$pb.TagNumber(6)
  $core.bool hasFib() => $_has(5);
  @$pb.TagNumber(6)
  void clearFib() => $_clearField(6);
  @$pb.TagNumber(6)
  FibResponse ensureFib() => $_ensure(5);

  @$pb.TagNumber(7)
  TimeResponse get time => $_getN(6);
  @$pb.TagNumber(7)
  set time(TimeResponse value) => $_setField(7, value);
  @$pb.TagNumber(7)
  $core.bool hasTime() => $_has(6);
  @$pb.TagNumber(7)
  void clearTime() => $_clearField(7);
  @$pb.TagNumber(7)
  TimeResponse ensureTime() => $_ensure(6);

  @$pb.TagNumber(8)
  NameResponse get name => $_getN(7);
  @$pb.TagNumber(8)
  set name(NameResponse value) => $_setField(8, value);
  @$pb.TagNumber(8)
  $core.bool hasName() => $_has(7);
  @$pb.TagNumber(8)
  void clearName() => $_clearField(8);
  @$pb.TagNumber(8)
  NameResponse ensureName() => $_ensure(7);

  @$pb.TagNumber(9)
  AckResponse get ack => $_getN(8);
  @$pb.TagNumber(9)
  set ack(AckResponse value) => $_setField(9, value);
  @$pb.TagNumber(9)
  $core.bool hasAck() => $_has(8);
  @$pb.TagNumber(9)
  void clearAck() => $_clearField(9);
  @$pb.TagNumber(9)
  AckResponse ensureAck() => $_ensure(8);

  @$pb.TagNumber(10)
  LedResponse get led => $_getN(9);
  @$pb.TagNumber(10)
  set led(LedResponse value) => $_setField(10, value);
  @$pb.TagNumber(10)
  $core.bool hasLed() => $_has(9);
  @$pb.TagNumber(10)
  void clearLed() => $_clearField(10);
  @$pb.TagNumber(10)
  LedResponse ensureLed() => $_ensure(9);
}

const $core.bool _omitFieldNames =
    $core.bool.fromEnvironment('protobuf.omit_field_names');
const $core.bool _omitMessageNames =
    $core.bool.fromEnvironment('protobuf.omit_message_names');
