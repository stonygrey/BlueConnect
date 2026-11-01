// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

/// A camera page that pops with the first QR code it sees. The
/// mobile_scanner plugin decodes with ML Kit on Android and AVFoundation on
/// Apple platforms, and asks for the camera permission itself.
class QrScanPage extends StatefulWidget {
  const QrScanPage({super.key});

  @override
  State<QrScanPage> createState() => _QrScanPageState();
}

class _QrScanPageState extends State<QrScanPage> {
  bool _delivered = false;

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Scan QR')),
        body: MobileScanner(
          onDetect: (capture) {
            if (_delivered) return;
            for (final barcode in capture.barcodes) {
              final value = barcode.rawValue;
              if (value != null && value.isNotEmpty) {
                _delivered = true;
                Navigator.of(context).pop(value);
                return;
              }
            }
          },
        ),
      );
}
