cmake_minimum_required(VERSION 3.20)

set(CMAKE_SYSTEM_NAME Generic)
set(CMAKE_SYSTEM_PROCESSOR arm)
set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)

if(NOT DEFINED NRF5_SDK_ROOT)
  message(FATAL_ERROR "Pass -DNRF5_SDK_ROOT=<path to nRF5_SDK_17.1.0_ddde560>")
endif()
if(NOT DEFINED ARM_GCC_ROOT)
  message(FATAL_ERROR "Pass -DARM_GCC_ROOT=<path containing bin/arm-none-eabi-gcc.exe>")
endif()

file(TO_CMAKE_PATH "${NRF5_SDK_ROOT}" SDK)
file(TO_CMAKE_PATH "${ARM_GCC_ROOT}" GCC)
set(CMAKE_C_COMPILER "${GCC}/bin/arm-none-eabi-gcc.exe")
set(CMAKE_ASM_COMPILER "${GCC}/bin/arm-none-eabi-gcc.exe")
set(CMAKE_OBJCOPY "${GCC}/bin/arm-none-eabi-objcopy.exe")
set(CMAKE_SIZE "${GCC}/bin/arm-none-eabi-size.exe")

project(epaper_ble_receiver C ASM)

add_executable(epaper_ble_receiver
  src/main.c
  src/panel.c
  ${SDK}/modules/nrfx/mdk/gcc_startup_nrf52810.S
  ${SDK}/modules/nrfx/mdk/system_nrf52810.c
  ${SDK}/components/libraries/util/app_error.c
  ${SDK}/components/libraries/util/app_error_weak.c
  ${SDK}/components/libraries/util/app_util_platform.c
  ${SDK}/components/libraries/util/nrf_assert.c
  ${SDK}/components/libraries/experimental_section_vars/nrf_section_iter.c
  ${SDK}/components/ble/common/ble_advdata.c
  ${SDK}/components/ble/common/ble_srv_common.c
  ${SDK}/components/softdevice/common/nrf_sdh.c
  ${SDK}/components/softdevice/common/nrf_sdh_ble.c
  ${SDK}/components/softdevice/common/nrf_sdh_soc.c
)

target_include_directories(epaper_ble_receiver PRIVATE
  src
  ${SDK}/examples/ble_peripheral/ble_app_blinky/pca10040e/s112/config
  ${SDK}/components
  ${SDK}/components/ble/common
  ${SDK}/components/libraries/util
  ${SDK}/components/libraries/delay
  ${SDK}/components/libraries/experimental_section_vars
  ${SDK}/components/libraries/log
  ${SDK}/components/libraries/log/src
  ${SDK}/components/libraries/strerror
  ${SDK}/components/softdevice/common
  ${SDK}/components/softdevice/s112/headers
  ${SDK}/components/softdevice/s112/headers/nrf52
  ${SDK}/components/toolchain/cmsis/include
  ${SDK}/integration/nrfx
  ${SDK}/integration/nrfx/legacy
  ${SDK}/modules/nrfx
  ${SDK}/modules/nrfx/hal
  ${SDK}/modules/nrfx/mdk
)

target_compile_definitions(epaper_ble_receiver PRIVATE
  BOARD_PCA10040
  CONFIG_GPIO_AS_PINRESET
  DEVELOP_IN_NRF52832
  FLOAT_ABI_SOFT
  NRF52810_XXAA
  NRF52_PAN_74
  NRF_SD_BLE_API_VERSION=7
  S112
  SOFTDEVICE_PRESENT
  NRF_LOG_ENABLED=0
  NRF_SDH_BLE_VS_UUID_COUNT=1
  NRF_SDH_BLE_GATT_MAX_MTU_SIZE=185
  NRF_SDH_BLE_GAP_DATA_LENGTH=27
  NRF_SDH_CLOCK_LF_SRC=0
  NRF_SDH_CLOCK_LF_RC_CTIV=16
  NRF_SDH_CLOCK_LF_RC_TEMP_CTIV=2
  NRF_SDH_CLOCK_LF_ACCURACY=1
  __HEAP_SIZE=0
  __STACK_SIZE=2048
)

target_compile_options(epaper_ble_receiver PRIVATE
  -Os -g3 -mcpu=cortex-m4 -mthumb -mabi=aapcs -mfloat-abi=soft
  -ffunction-sections -fdata-sections -fno-strict-aliasing -fno-builtin
  -fshort-enums -Wall -Wextra
)

target_link_options(epaper_ble_receiver PRIVATE
  -mcpu=cortex-m4 -mthumb -mabi=aapcs -mfloat-abi=soft
  -T${CMAKE_CURRENT_SOURCE_DIR}/epaper_ble_gcc_nrf52.ld
  -L${SDK}/modules/nrfx/mdk
  -Wl,--gc-sections -Wl,-Map=${CMAKE_CURRENT_BINARY_DIR}/epaper_ble_receiver.map
  --specs=nano.specs
)

target_link_libraries(epaper_ble_receiver PRIVATE c nosys m)
set_target_properties(epaper_ble_receiver PROPERTIES SUFFIX ".elf")

add_custom_command(TARGET epaper_ble_receiver POST_BUILD
  COMMAND ${CMAKE_OBJCOPY} -O ihex $<TARGET_FILE:epaper_ble_receiver> ${CMAKE_CURRENT_BINARY_DIR}/epaper_ble_receiver.hex
  COMMAND ${CMAKE_OBJCOPY} -O binary $<TARGET_FILE:epaper_ble_receiver> ${CMAKE_CURRENT_BINARY_DIR}/epaper_ble_receiver.bin
  COMMAND ${CMAKE_SIZE} $<TARGET_FILE:epaper_ble_receiver>
  VERBATIM
)
