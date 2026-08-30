# Copyright © 2026 Stony Grey LLC.
# SPDX-License-Identifier: MIT
# EDHOC firmware: peripheral_uart (NCS) chassis plus the shared Verdigris
# GATT service (firmware/common) with an EDHOC responder (edhoc.c) behind it,
# switchable via CONFIG_VERDIGRIS.
cmake_minimum_required(VERSION 3.20.0)

find_package(Zephyr REQUIRED HINTS $ENV{ZEPHYR_BASE})
project(edhoc_firmware)

# The proto lives at the repo root (single source of truth with the C# side);
# nanopb wants it and its .options side by side under the app tree.
configure_file(${CMAKE_CURRENT_SOURCE_DIR}/../../proto/verdigris.proto
               ${CMAKE_CURRENT_SOURCE_DIR}/src/verdigris.proto COPYONLY)
configure_file(${CMAKE_CURRENT_SOURCE_DIR}/../../proto/verdigris.options
               ${CMAKE_CURRENT_SOURCE_DIR}/src/verdigris.options COPYONLY)

if(CONFIG_VERDIGRIS)
  list(APPEND CMAKE_MODULE_PATH ${ZEPHYR_BASE}/modules/nanopb)
  include(nanopb)
  zephyr_nanopb_sources(app src/verdigris.proto)
  # The Verdigris service (GATT table, dispatcher, framing) is shared with
  # firmware/noise and firmware/pake; this app supplies the EDHOC responder
  # behind link_iface.h.
  target_include_directories(app PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../common/src)
  target_sources(app PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../common/src/verdigris.c
    ${CMAKE_CURRENT_SOURCE_DIR}/../common/src/segment.c
    src/edhoc.c
    src/cred_ui.c)
  # ecdh_x decompresses x-only peer points via ocrypto. On SoCs whose PSA
  # driver is CRACEN (nRF54L) the Oberon library is not already in the link,
  # so link the standalone nrf_oberon (CONFIG_NRF_OBERON in prj.conf).
  target_link_libraries(app PRIVATE nrfxlib_crypto)
endif()

# The peripheral_uart chassis is shared; src/main.c is the app-init shim.
target_sources(app PRIVATE
  ${CMAKE_CURRENT_SOURCE_DIR}/../common/src/app_main.c
  src/main.c)
