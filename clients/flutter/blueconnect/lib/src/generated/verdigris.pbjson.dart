// This is a generated file - do not edit.
//
// Generated from verdigris.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports
// ignore_for_file: unused_import

import 'dart:convert' as $convert;
import 'dart:core' as $core;
import 'dart:typed_data' as $typed_data;

@$core.Deprecated('Use statusDescriptor instead')
const Status$json = {
  '1': 'Status',
  '2': [
    {'1': 'STATUS_OK', '2': 0},
    {'1': 'STATUS_UNKNOWN_OP', '2': 1},
    {'1': 'STATUS_INVALID_ARGUMENT', '2': 2},
    {'1': 'STATUS_OUT_OF_RANGE', '2': 3},
    {'1': 'STATUS_INTERNAL', '2': 4},
    {'1': 'STATUS_BUSY', '2': 5},
    {'1': 'STATUS_UNAUTHENTICATED', '2': 6},
    {'1': 'STATUS_PERMISSION_DENIED', '2': 7},
  ],
};

/// Descriptor for `Status`. Decode as a `google.protobuf.EnumDescriptorProto`.
final $typed_data.Uint8List statusDescriptor = $convert.base64Decode(
    'CgZTdGF0dXMSDQoJU1RBVFVTX09LEAASFQoRU1RBVFVTX1VOS05PV05fT1AQARIbChdTVEFUVV'
    'NfSU5WQUxJRF9BUkdVTUVOVBACEhcKE1NUQVRVU19PVVRfT0ZfUkFOR0UQAxITCg9TVEFUVVNf'
    'SU5URVJOQUwQBBIPCgtTVEFUVVNfQlVTWRAFEhoKFlNUQVRVU19VTkFVVEhFTlRJQ0FURUQQBh'
    'IcChhTVEFUVVNfUEVSTUlTU0lPTl9ERU5JRUQQBw==');

@$core.Deprecated('Use privilegeDescriptor instead')
const Privilege$json = {
  '1': 'Privilege',
  '2': [
    {'1': 'PRIVILEGE_LOW', '2': 0},
    {'1': 'PRIVILEGE_HIGH', '2': 1},
  ],
};

/// Descriptor for `Privilege`. Decode as a `google.protobuf.EnumDescriptorProto`.
final $typed_data.Uint8List privilegeDescriptor = $convert.base64Decode(
    'CglQcml2aWxlZ2USEQoNUFJJVklMRUdFX0xPVxAAEhIKDlBSSVZJTEVHRV9ISUdIEAE=');

@$core.Deprecated('Use echoRequestDescriptor instead')
const EchoRequest$json = {
  '1': 'EchoRequest',
  '2': [
    {'1': 'payload', '3': 1, '4': 1, '5': 12, '10': 'payload'},
  ],
};

/// Descriptor for `EchoRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List echoRequestDescriptor = $convert
    .base64Decode('CgtFY2hvUmVxdWVzdBIYCgdwYXlsb2FkGAEgASgMUgdwYXlsb2Fk');

@$core.Deprecated('Use echoResponseDescriptor instead')
const EchoResponse$json = {
  '1': 'EchoResponse',
  '2': [
    {'1': 'payload', '3': 1, '4': 1, '5': 12, '10': 'payload'},
  ],
};

/// Descriptor for `EchoResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List echoResponseDescriptor = $convert
    .base64Decode('CgxFY2hvUmVzcG9uc2USGAoHcGF5bG9hZBgBIAEoDFIHcGF5bG9hZA==');

@$core.Deprecated('Use sqrtRequestDescriptor instead')
const SqrtRequest$json = {
  '1': 'SqrtRequest',
  '2': [
    {'1': 'value', '3': 1, '4': 1, '5': 1, '10': 'value'},
  ],
};

/// Descriptor for `SqrtRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List sqrtRequestDescriptor =
    $convert.base64Decode('CgtTcXJ0UmVxdWVzdBIUCgV2YWx1ZRgBIAEoAVIFdmFsdWU=');

@$core.Deprecated('Use sqrtResponseDescriptor instead')
const SqrtResponse$json = {
  '1': 'SqrtResponse',
  '2': [
    {'1': 'value', '3': 1, '4': 1, '5': 1, '10': 'value'},
  ],
};

/// Descriptor for `SqrtResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List sqrtResponseDescriptor =
    $convert.base64Decode('CgxTcXJ0UmVzcG9uc2USFAoFdmFsdWUYASABKAFSBXZhbHVl');

@$core.Deprecated('Use fibRequestDescriptor instead')
const FibRequest$json = {
  '1': 'FibRequest',
  '2': [
    {'1': 'n', '3': 1, '4': 1, '5': 13, '10': 'n'},
  ],
};

/// Descriptor for `FibRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List fibRequestDescriptor =
    $convert.base64Decode('CgpGaWJSZXF1ZXN0EgwKAW4YASABKA1SAW4=');

@$core.Deprecated('Use fibResponseDescriptor instead')
const FibResponse$json = {
  '1': 'FibResponse',
  '2': [
    {'1': 'value', '3': 1, '4': 1, '5': 4, '10': 'value'},
  ],
};

/// Descriptor for `FibResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List fibResponseDescriptor =
    $convert.base64Decode('CgtGaWJSZXNwb25zZRIUCgV2YWx1ZRgBIAEoBFIFdmFsdWU=');

@$core.Deprecated('Use getTimeRequestDescriptor instead')
const GetTimeRequest$json = {
  '1': 'GetTimeRequest',
};

/// Descriptor for `GetTimeRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List getTimeRequestDescriptor =
    $convert.base64Decode('Cg5HZXRUaW1lUmVxdWVzdA==');

@$core.Deprecated('Use setTimeRequestDescriptor instead')
const SetTimeRequest$json = {
  '1': 'SetTimeRequest',
  '2': [
    {'1': 'unix_millis', '3': 1, '4': 1, '5': 3, '10': 'unixMillis'},
  ],
};

/// Descriptor for `SetTimeRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List setTimeRequestDescriptor = $convert.base64Decode(
    'Cg5TZXRUaW1lUmVxdWVzdBIfCgt1bml4X21pbGxpcxgBIAEoA1IKdW5peE1pbGxpcw==');

@$core.Deprecated('Use timeResponseDescriptor instead')
const TimeResponse$json = {
  '1': 'TimeResponse',
  '2': [
    {'1': 'unix_millis', '3': 1, '4': 1, '5': 3, '10': 'unixMillis'},
  ],
};

/// Descriptor for `TimeResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List timeResponseDescriptor = $convert.base64Decode(
    'CgxUaW1lUmVzcG9uc2USHwoLdW5peF9taWxsaXMYASABKANSCnVuaXhNaWxsaXM=');

@$core.Deprecated('Use setSerialNumberRequestDescriptor instead')
const SetSerialNumberRequest$json = {
  '1': 'SetSerialNumberRequest',
  '2': [
    {'1': 'serial_number', '3': 1, '4': 1, '5': 13, '10': 'serialNumber'},
  ],
};

/// Descriptor for `SetSerialNumberRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List setSerialNumberRequestDescriptor =
    $convert.base64Decode(
        'ChZTZXRTZXJpYWxOdW1iZXJSZXF1ZXN0EiMKDXNlcmlhbF9udW1iZXIYASABKA1SDHNlcmlhbE'
        '51bWJlcg==');

@$core.Deprecated('Use getSerialNumberRequestDescriptor instead')
const GetSerialNumberRequest$json = {
  '1': 'GetSerialNumberRequest',
};

/// Descriptor for `GetSerialNumberRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List getSerialNumberRequestDescriptor =
    $convert.base64Decode('ChZHZXRTZXJpYWxOdW1iZXJSZXF1ZXN0');

@$core.Deprecated('Use nameResponseDescriptor instead')
const NameResponse$json = {
  '1': 'NameResponse',
  '2': [
    {'1': 'name', '3': 1, '4': 1, '5': 9, '10': 'name'},
  ],
};

/// Descriptor for `NameResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List nameResponseDescriptor =
    $convert.base64Decode('CgxOYW1lUmVzcG9uc2USEgoEbmFtZRgBIAEoCVIEbmFtZQ==');

@$core.Deprecated('Use getLedRequestDescriptor instead')
const GetLedRequest$json = {
  '1': 'GetLedRequest',
};

/// Descriptor for `GetLedRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List getLedRequestDescriptor =
    $convert.base64Decode('Cg1HZXRMZWRSZXF1ZXN0');

@$core.Deprecated('Use setLedRequestDescriptor instead')
const SetLedRequest$json = {
  '1': 'SetLedRequest',
  '2': [
    {'1': 'on', '3': 1, '4': 1, '5': 8, '10': 'on'},
  ],
};

/// Descriptor for `SetLedRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List setLedRequestDescriptor =
    $convert.base64Decode('Cg1TZXRMZWRSZXF1ZXN0Eg4KAm9uGAEgASgIUgJvbg==');

@$core.Deprecated('Use ledResponseDescriptor instead')
const LedResponse$json = {
  '1': 'LedResponse',
  '2': [
    {'1': 'on', '3': 1, '4': 1, '5': 8, '10': 'on'},
  ],
};

/// Descriptor for `LedResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List ledResponseDescriptor =
    $convert.base64Decode('CgtMZWRSZXNwb25zZRIOCgJvbhgBIAEoCFICb24=');

@$core.Deprecated('Use pairRequestDescriptor instead')
const PairRequest$json = {
  '1': 'PairRequest',
  '2': [
    {
      '1': 'privilege',
      '3': 1,
      '4': 1,
      '5': 14,
      '6': '.verdigris.Privilege',
      '10': 'privilege'
    },
  ],
};

/// Descriptor for `PairRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List pairRequestDescriptor = $convert.base64Decode(
    'CgtQYWlyUmVxdWVzdBIyCglwcml2aWxlZ2UYASABKA4yFC52ZXJkaWdyaXMuUHJpdmlsZWdlUg'
    'lwcml2aWxlZ2U=');

@$core.Deprecated('Use unpairRequestDescriptor instead')
const UnpairRequest$json = {
  '1': 'UnpairRequest',
};

/// Descriptor for `UnpairRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List unpairRequestDescriptor =
    $convert.base64Decode('Cg1VbnBhaXJSZXF1ZXN0');

@$core.Deprecated('Use ackResponseDescriptor instead')
const AckResponse$json = {
  '1': 'AckResponse',
};

/// Descriptor for `AckResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List ackResponseDescriptor =
    $convert.base64Decode('CgtBY2tSZXNwb25zZQ==');

@$core.Deprecated('Use enrollRequestDescriptor instead')
const EnrollRequest$json = {
  '1': 'EnrollRequest',
  '2': [
    {
      '1': 'client_public_key',
      '3': 1,
      '4': 1,
      '5': 12,
      '10': 'clientPublicKey'
    },
  ],
};

/// Descriptor for `EnrollRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List enrollRequestDescriptor = $convert.base64Decode(
    'Cg1FbnJvbGxSZXF1ZXN0EioKEWNsaWVudF9wdWJsaWNfa2V5GAEgASgMUg9jbGllbnRQdWJsaW'
    'NLZXk=');

@$core.Deprecated('Use requestDescriptor instead')
const Request$json = {
  '1': 'Request',
  '2': [
    {'1': 'id', '3': 1, '4': 1, '5': 13, '10': 'id'},
    {
      '1': 'echo',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.verdigris.EchoRequest',
      '9': 0,
      '10': 'echo'
    },
    {
      '1': 'sqrt',
      '3': 3,
      '4': 1,
      '5': 11,
      '6': '.verdigris.SqrtRequest',
      '9': 0,
      '10': 'sqrt'
    },
    {
      '1': 'fib',
      '3': 4,
      '4': 1,
      '5': 11,
      '6': '.verdigris.FibRequest',
      '9': 0,
      '10': 'fib'
    },
    {
      '1': 'get_time',
      '3': 5,
      '4': 1,
      '5': 11,
      '6': '.verdigris.GetTimeRequest',
      '9': 0,
      '10': 'getTime'
    },
    {
      '1': 'set_time',
      '3': 6,
      '4': 1,
      '5': 11,
      '6': '.verdigris.SetTimeRequest',
      '9': 0,
      '10': 'setTime'
    },
    {
      '1': 'set_serial_number',
      '3': 7,
      '4': 1,
      '5': 11,
      '6': '.verdigris.SetSerialNumberRequest',
      '9': 0,
      '10': 'setSerialNumber'
    },
    {
      '1': 'pair',
      '3': 8,
      '4': 1,
      '5': 11,
      '6': '.verdigris.PairRequest',
      '9': 0,
      '10': 'pair'
    },
    {
      '1': 'unpair',
      '3': 9,
      '4': 1,
      '5': 11,
      '6': '.verdigris.UnpairRequest',
      '9': 0,
      '10': 'unpair'
    },
    {
      '1': 'get_serial_number',
      '3': 10,
      '4': 1,
      '5': 11,
      '6': '.verdigris.GetSerialNumberRequest',
      '9': 0,
      '10': 'getSerialNumber'
    },
    {
      '1': 'get_led',
      '3': 11,
      '4': 1,
      '5': 11,
      '6': '.verdigris.GetLedRequest',
      '9': 0,
      '10': 'getLed'
    },
    {
      '1': 'set_led',
      '3': 12,
      '4': 1,
      '5': 11,
      '6': '.verdigris.SetLedRequest',
      '9': 0,
      '10': 'setLed'
    },
    {
      '1': 'enroll',
      '3': 13,
      '4': 1,
      '5': 11,
      '6': '.verdigris.EnrollRequest',
      '9': 0,
      '10': 'enroll'
    },
  ],
  '8': [
    {'1': 'op'},
  ],
};

/// Descriptor for `Request`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List requestDescriptor = $convert.base64Decode(
    'CgdSZXF1ZXN0Eg4KAmlkGAEgASgNUgJpZBIsCgRlY2hvGAIgASgLMhYudmVyZGlncmlzLkVjaG'
    '9SZXF1ZXN0SABSBGVjaG8SLAoEc3FydBgDIAEoCzIWLnZlcmRpZ3Jpcy5TcXJ0UmVxdWVzdEgA'
    'UgRzcXJ0EikKA2ZpYhgEIAEoCzIVLnZlcmRpZ3Jpcy5GaWJSZXF1ZXN0SABSA2ZpYhI2CghnZX'
    'RfdGltZRgFIAEoCzIZLnZlcmRpZ3Jpcy5HZXRUaW1lUmVxdWVzdEgAUgdnZXRUaW1lEjYKCHNl'
    'dF90aW1lGAYgASgLMhkudmVyZGlncmlzLlNldFRpbWVSZXF1ZXN0SABSB3NldFRpbWUSTwoRc2'
    'V0X3NlcmlhbF9udW1iZXIYByABKAsyIS52ZXJkaWdyaXMuU2V0U2VyaWFsTnVtYmVyUmVxdWVz'
    'dEgAUg9zZXRTZXJpYWxOdW1iZXISLAoEcGFpchgIIAEoCzIWLnZlcmRpZ3Jpcy5QYWlyUmVxdW'
    'VzdEgAUgRwYWlyEjIKBnVucGFpchgJIAEoCzIYLnZlcmRpZ3Jpcy5VbnBhaXJSZXF1ZXN0SABS'
    'BnVucGFpchJPChFnZXRfc2VyaWFsX251bWJlchgKIAEoCzIhLnZlcmRpZ3Jpcy5HZXRTZXJpYW'
    'xOdW1iZXJSZXF1ZXN0SABSD2dldFNlcmlhbE51bWJlchIzCgdnZXRfbGVkGAsgASgLMhgudmVy'
    'ZGlncmlzLkdldExlZFJlcXVlc3RIAFIGZ2V0TGVkEjMKB3NldF9sZWQYDCABKAsyGC52ZXJkaW'
    'dyaXMuU2V0TGVkUmVxdWVzdEgAUgZzZXRMZWQSMgoGZW5yb2xsGA0gASgLMhgudmVyZGlncmlz'
    'LkVucm9sbFJlcXVlc3RIAFIGZW5yb2xsQgQKAm9w');

@$core.Deprecated('Use responseDescriptor instead')
const Response$json = {
  '1': 'Response',
  '2': [
    {'1': 'id', '3': 1, '4': 1, '5': 13, '10': 'id'},
    {
      '1': 'status',
      '3': 2,
      '4': 1,
      '5': 14,
      '6': '.verdigris.Status',
      '10': 'status'
    },
    {'1': 'detail', '3': 3, '4': 1, '5': 9, '10': 'detail'},
    {
      '1': 'echo',
      '3': 4,
      '4': 1,
      '5': 11,
      '6': '.verdigris.EchoResponse',
      '9': 0,
      '10': 'echo'
    },
    {
      '1': 'sqrt',
      '3': 5,
      '4': 1,
      '5': 11,
      '6': '.verdigris.SqrtResponse',
      '9': 0,
      '10': 'sqrt'
    },
    {
      '1': 'fib',
      '3': 6,
      '4': 1,
      '5': 11,
      '6': '.verdigris.FibResponse',
      '9': 0,
      '10': 'fib'
    },
    {
      '1': 'time',
      '3': 7,
      '4': 1,
      '5': 11,
      '6': '.verdigris.TimeResponse',
      '9': 0,
      '10': 'time'
    },
    {
      '1': 'name',
      '3': 8,
      '4': 1,
      '5': 11,
      '6': '.verdigris.NameResponse',
      '9': 0,
      '10': 'name'
    },
    {
      '1': 'ack',
      '3': 9,
      '4': 1,
      '5': 11,
      '6': '.verdigris.AckResponse',
      '9': 0,
      '10': 'ack'
    },
    {
      '1': 'led',
      '3': 10,
      '4': 1,
      '5': 11,
      '6': '.verdigris.LedResponse',
      '9': 0,
      '10': 'led'
    },
  ],
  '8': [
    {'1': 'result'},
  ],
};

/// Descriptor for `Response`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List responseDescriptor = $convert.base64Decode(
    'CghSZXNwb25zZRIOCgJpZBgBIAEoDVICaWQSKQoGc3RhdHVzGAIgASgOMhEudmVyZGlncmlzLl'
    'N0YXR1c1IGc3RhdHVzEhYKBmRldGFpbBgDIAEoCVIGZGV0YWlsEi0KBGVjaG8YBCABKAsyFy52'
    'ZXJkaWdyaXMuRWNob1Jlc3BvbnNlSABSBGVjaG8SLQoEc3FydBgFIAEoCzIXLnZlcmRpZ3Jpcy'
    '5TcXJ0UmVzcG9uc2VIAFIEc3FydBIqCgNmaWIYBiABKAsyFi52ZXJkaWdyaXMuRmliUmVzcG9u'
    'c2VIAFIDZmliEi0KBHRpbWUYByABKAsyFy52ZXJkaWdyaXMuVGltZVJlc3BvbnNlSABSBHRpbW'
    'USLQoEbmFtZRgIIAEoCzIXLnZlcmRpZ3Jpcy5OYW1lUmVzcG9uc2VIAFIEbmFtZRIqCgNhY2sY'
    'CSABKAsyFi52ZXJkaWdyaXMuQWNrUmVzcG9uc2VIAFIDYWNrEioKA2xlZBgKIAEoCzIWLnZlcm'
    'RpZ3Jpcy5MZWRSZXNwb25zZUgAUgNsZWRCCAoGcmVzdWx0');
