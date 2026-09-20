/* nRF52811 photo viewer: bootloader 0x0000-0x3FFF, viewer below 0x1CFF0. */
SEARCH_DIR(.)
GROUP(-lgcc -lc -lnosys)

MEMORY
{
  FLASH (rx) : ORIGIN = 0x00004000, LENGTH = 0x18FF0
  VIEWER_ID (rx) : ORIGIN = 0x0001CFF0, LENGTH = 0x10
  RAM (rwx) : ORIGIN = 0x20000000, LENGTH = 0x6000
  CODE_RAM (rwx) : ORIGIN = 0x800000, LENGTH = 0x6000
}

SECTIONS
{
  .viewer_id :
  {
    KEEP(*(.viewer_id))
  } > VIEWER_ID
}

INCLUDE "nrf_common.ld"
