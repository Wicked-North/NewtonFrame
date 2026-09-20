#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "app_error.h"
#include "ble.h"
#include "ble_advdata.h"
#include "ble_hci.h"
#include "ble_srv_common.h"
#include "nrf.h"
#include "nrf_gpio.h"
#include "nrf_sdh.h"
#include "nrf_sdh_ble.h"
#include "nrf_soc.h"
#include "panel.h"

#define DEVICE_NAME "EPHOTO-648"
#define APP_BLE_CONN_CFG_TAG 1
#define APP_BLE_OBSERVER_PRIO 3
#define IMAGE_WIDTH 648u
#define IMAGE_HEIGHT 480u
#define IMAGE_SIZE (IMAGE_WIDTH * IMAGE_HEIGHT / 4u)
#define PROTOCOL_VERSION 1u
#define ADV_INTERVAL 160u /* 100 ms in 0.625 ms units. */
#define ADV_DURATION 6000u /* 60 seconds in 10 ms units. */
#define WAKE_BUTTON_1 28u
#define WAKE_BUTTON_2 29u

#define PHOTO_UUID_SERVICE 0x0000u
#define PHOTO_UUID_CONTROL 0x0001u
#define PHOTO_UUID_DATA 0x0002u
#define PHOTO_UUID_STATUS 0x0003u
#define PHOTO_UUID_BASE {0x01, 0xE0, 0x80, 0x84, 0x64, 0x2C, 0xB7, 0xA2, \
                         0x4B, 0x4F, 0x8A, 0x6E, 0x00, 0x00, 0x1E, 0x7B}

typedef enum {
    STATE_IDLE = 0,
    STATE_PREPARING = 1,
    STATE_RECEIVING = 2,
    STATE_VERIFYING = 3,
    STATE_REFRESHING = 4,
    STATE_COMPLETE = 5,
    STATE_ERROR = 255
} transfer_state_t;

typedef enum {
    ERROR_NONE = 0,
    ERROR_INVALID_HEADER = 1,
    ERROR_WRONG_OFFSET = 2,
    ERROR_TOO_MUCH_DATA = 3,
    ERROR_CRC_MISMATCH = 4,
    ERROR_NOT_READY = 5,
    ERROR_PANEL_TIMEOUT = 6
} transfer_error_t;

static uint16_t m_conn_handle = BLE_CONN_HANDLE_INVALID;
static uint8_t m_uuid_type;
static uint16_t m_service_handle;
static ble_gatts_char_handles_t m_control_handles;
static ble_gatts_char_handles_t m_data_handles;
static ble_gatts_char_handles_t m_status_handles;
static uint8_t m_adv_handle = BLE_GAP_ADV_SET_HANDLE_NOT_SET;
static uint8_t m_adv_buffer[BLE_GAP_ADV_SET_DATA_SIZE_MAX];
static uint8_t m_scan_buffer[BLE_GAP_ADV_SET_DATA_SIZE_MAX];
static bool m_status_notifications;
static transfer_state_t m_state = STATE_IDLE;
static transfer_error_t m_error = ERROR_NONE;
static uint32_t m_expected_offset;
static uint32_t m_expected_crc;
static uint32_t m_running_crc = 0xFFFFFFFFu;
static volatile bool m_prepare_pending;
static volatile bool m_finish_pending;
static volatile bool m_abort_pending;
static volatile bool m_disconnect_pending;
static volatile bool m_system_off_pending;
static bool m_shutdown_after_disconnect;

static void enter_system_off(void) {
    nrf_gpio_cfg_sense_input(WAKE_BUTTON_1,
                             NRF_GPIO_PIN_PULLUP,
                             NRF_GPIO_PIN_SENSE_LOW);
    nrf_gpio_cfg_sense_input(WAKE_BUTTON_2,
                             NRF_GPIO_PIN_PULLUP,
                             NRF_GPIO_PIN_SENSE_LOW);
    (void)sd_power_system_off();
    for (;;) {
    }
}

static uint32_t read_le32(uint8_t const *data) {
    return (uint32_t)data[0] |
           ((uint32_t)data[1] << 8) |
           ((uint32_t)data[2] << 16) |
           ((uint32_t)data[3] << 24);
}

static uint16_t read_le16(uint8_t const *data) {
    return (uint16_t)data[0] | ((uint16_t)data[1] << 8);
}

static void write_le32(uint8_t *data, uint32_t value) {
    data[0] = (uint8_t)value;
    data[1] = (uint8_t)(value >> 8);
    data[2] = (uint8_t)(value >> 16);
    data[3] = (uint8_t)(value >> 24);
}

static uint32_t crc32_update(uint32_t crc, uint8_t const *data, uint16_t length) {
    while (length--) {
        crc ^= *data++;
        for (uint8_t bit = 0; bit < 8; ++bit) {
            crc = (crc >> 1) ^ (0xEDB88320u & (uint32_t)-(int32_t)(crc & 1u));
        }
    }
    return crc;
}

static void status_publish(void) {
    uint8_t value[12] = {PROTOCOL_VERSION, (uint8_t)m_state, 0, 0};
    value[2] = (uint8_t)m_error;
    value[3] = (uint8_t)((uint16_t)m_error >> 8);
    write_le32(&value[4], m_expected_offset);
    write_le32(&value[8], m_running_crc ^ 0xFFFFFFFFu);

    ble_gatts_value_t gatts_value = {
        .len = sizeof(value),
        .offset = 0,
        .p_value = value
    };
    APP_ERROR_CHECK(sd_ble_gatts_value_set(BLE_CONN_HANDLE_INVALID,
                                            m_status_handles.value_handle,
                                            &gatts_value));

    if (m_conn_handle != BLE_CONN_HANDLE_INVALID && m_status_notifications) {
        uint16_t length = sizeof(value);
        ble_gatts_hvx_params_t hvx = {
            .handle = m_status_handles.value_handle,
            .type = BLE_GATT_HVX_NOTIFICATION,
            .offset = 0,
            .p_len = &length,
            .p_data = value
        };
        uint32_t result = sd_ble_gatts_hvx(m_conn_handle, &hvx);
        if (result != NRF_SUCCESS && result != NRF_ERROR_RESOURCES &&
            result != NRF_ERROR_INVALID_STATE) {
            APP_ERROR_CHECK(result);
        }
    }
}

static void fail(transfer_error_t error) {
    m_state = STATE_ERROR;
    m_error = error;
    status_publish();
}

static void handle_control(uint8_t const *data, uint16_t length) {
    if (length == 0) return;

    if (data[0] == 0x01u) {
        if (length != 16u || data[1] != PROTOCOL_VERSION ||
            read_le16(&data[2]) != IMAGE_WIDTH ||
            read_le16(&data[4]) != IMAGE_HEIGHT || data[6] != 0x01u ||
            data[7] != 0u || read_le32(&data[8]) != IMAGE_SIZE) {
            fail(ERROR_INVALID_HEADER);
            return;
        }
        m_state = STATE_PREPARING;
        m_error = ERROR_NONE;
        m_expected_offset = 0;
        m_expected_crc = read_le32(&data[12]);
        m_running_crc = 0xFFFFFFFFu;
        status_publish();
        m_prepare_pending = true;
        return;
    }

    if (data[0] == 0x02u && length == 1u) {
        if (m_state != STATE_RECEIVING || m_expected_offset != IMAGE_SIZE) {
            fail(ERROR_NOT_READY);
            return;
        }
        m_state = STATE_VERIFYING;
        status_publish();
        if ((m_running_crc ^ 0xFFFFFFFFu) != m_expected_crc) {
            fail(ERROR_CRC_MISMATCH);
            return;
        }
        m_state = STATE_REFRESHING;
        status_publish();
        m_finish_pending = true;
        return;
    }

    if (data[0] == 0x03u && length == 1u) {
        m_state = STATE_IDLE;
        m_error = ERROR_NONE;
        m_expected_offset = 0;
        m_running_crc = 0xFFFFFFFFu;
        m_abort_pending = true;
        status_publish();
        return;
    }

    fail(ERROR_INVALID_HEADER);
}

static void handle_data(uint8_t const *data, uint16_t length) {
    if (m_state != STATE_RECEIVING || length < 5u) {
        fail(ERROR_NOT_READY);
        return;
    }

    uint32_t offset = read_le32(data);
    uint16_t payload_length = length - 4u;
    if (offset != m_expected_offset) {
        fail(ERROR_WRONG_OFFSET);
        return;
    }
    if (m_expected_offset + payload_length > IMAGE_SIZE) {
        fail(ERROR_TOO_MUCH_DATA);
        return;
    }

    panel_write_stream(&data[4], payload_length);
    m_running_crc = crc32_update(m_running_crc, &data[4], payload_length);
    m_expected_offset += payload_length;

    if ((m_expected_offset & 0xFFu) == 0u || m_expected_offset == IMAGE_SIZE) {
        status_publish();
    }
}

static void photo_characteristic_add(uint16_t uuid,
                                     bool writable,
                                     bool write_without_response,
                                     bool readable,
                                     bool notify,
                                     uint16_t max_length,
                                     ble_gatts_char_handles_t *handles) {
    uint8_t initial_value[12] = {0};
    ble_uuid_t ble_uuid = {.uuid = uuid, .type = m_uuid_type};
    ble_gatts_char_md_t char_md;
    ble_gatts_attr_md_t attr_md;
    ble_gatts_attr_md_t cccd_md;
    ble_gatts_attr_t attr;
    memset(&char_md, 0, sizeof(char_md));
    memset(&attr_md, 0, sizeof(attr_md));
    memset(&cccd_md, 0, sizeof(cccd_md));
    memset(&attr, 0, sizeof(attr));

    char_md.char_props.read = readable;
    char_md.char_props.write = writable;
    char_md.char_props.write_wo_resp = write_without_response;
    char_md.char_props.notify = notify;

    if (notify) {
        BLE_GAP_CONN_SEC_MODE_SET_OPEN(&cccd_md.read_perm);
        BLE_GAP_CONN_SEC_MODE_SET_OPEN(&cccd_md.write_perm);
        cccd_md.vloc = BLE_GATTS_VLOC_STACK;
        char_md.p_cccd_md = &cccd_md;
    }

    BLE_GAP_CONN_SEC_MODE_SET_OPEN(&attr_md.read_perm);
    BLE_GAP_CONN_SEC_MODE_SET_OPEN(&attr_md.write_perm);
    attr_md.vloc = BLE_GATTS_VLOC_STACK;
    attr_md.vlen = max_length > 1u;

    attr.p_uuid = &ble_uuid;
    attr.p_attr_md = &attr_md;
    attr.init_len = readable ? 12u : 0u;
    attr.max_len = max_length;
    attr.p_value = readable ? initial_value : NULL;

    APP_ERROR_CHECK(sd_ble_gatts_characteristic_add(m_service_handle, &char_md, &attr, handles));
}

static void services_init(void) {
    ble_uuid128_t base_uuid = {PHOTO_UUID_BASE};
    APP_ERROR_CHECK(sd_ble_uuid_vs_add(&base_uuid, &m_uuid_type));

    ble_uuid_t service_uuid = {.uuid = PHOTO_UUID_SERVICE, .type = m_uuid_type};
    APP_ERROR_CHECK(sd_ble_gatts_service_add(BLE_GATTS_SRVC_TYPE_PRIMARY,
                                              &service_uuid,
                                              &m_service_handle));

    photo_characteristic_add(PHOTO_UUID_CONTROL, true, false, false, false, 16u, &m_control_handles);
    photo_characteristic_add(PHOTO_UUID_DATA, true, true, false, false, 20u, &m_data_handles);
    photo_characteristic_add(PHOTO_UUID_STATUS, false, false, true, true, 12u, &m_status_handles);
    status_publish();
}

static void gap_init(void) {
    ble_gap_conn_sec_mode_t security;
    BLE_GAP_CONN_SEC_MODE_SET_OPEN(&security);
    APP_ERROR_CHECK(sd_ble_gap_device_name_set(&security,
                                                (uint8_t const *)DEVICE_NAME,
                                                sizeof(DEVICE_NAME) - 1u));

    ble_gap_conn_params_t params;
    memset(&params, 0, sizeof(params));
    params.min_conn_interval = 12u; /* 15 ms */
    params.max_conn_interval = 24u; /* 30 ms */
    params.slave_latency = 0;
    params.conn_sup_timeout = 400u; /* 4 s */
    APP_ERROR_CHECK(sd_ble_gap_ppcp_set(&params));
}

static void advertising_init(void) {
    ble_advdata_t adv;
    ble_advdata_t scan;
    memset(&adv, 0, sizeof(adv));
    memset(&scan, 0, sizeof(scan));

    ble_uuid_t advertised_uuid = {.uuid = PHOTO_UUID_SERVICE, .type = m_uuid_type};
    adv.name_type = BLE_ADVDATA_FULL_NAME;
    adv.flags = BLE_GAP_ADV_FLAGS_LE_ONLY_GENERAL_DISC_MODE;
    scan.uuids_complete.uuid_cnt = 1;
    scan.uuids_complete.p_uuids = &advertised_uuid;

    ble_gap_adv_data_t data = {
        .adv_data = {.p_data = m_adv_buffer, .len = sizeof(m_adv_buffer)},
        .scan_rsp_data = {.p_data = m_scan_buffer, .len = sizeof(m_scan_buffer)}
    };
    APP_ERROR_CHECK(ble_advdata_encode(&adv, data.adv_data.p_data, &data.adv_data.len));
    APP_ERROR_CHECK(ble_advdata_encode(&scan, data.scan_rsp_data.p_data,
                                        &data.scan_rsp_data.len));

    ble_gap_adv_params_t params;
    memset(&params, 0, sizeof(params));
    params.properties.type = BLE_GAP_ADV_TYPE_CONNECTABLE_SCANNABLE_UNDIRECTED;
    params.primary_phy = BLE_GAP_PHY_1MBPS;
    params.interval = ADV_INTERVAL;
    params.duration = ADV_DURATION;
    params.filter_policy = BLE_GAP_ADV_FP_ANY;
    APP_ERROR_CHECK(sd_ble_gap_adv_set_configure(&m_adv_handle, &data, &params));
}

static void advertising_start(void) {
    APP_ERROR_CHECK(sd_ble_gap_adv_start(m_adv_handle, APP_BLE_CONN_CFG_TAG));
}

static void ble_evt_handler(ble_evt_t const *event, void *context) {
    (void)context;
    switch (event->header.evt_id) {
        case BLE_GAP_EVT_CONNECTED:
            m_conn_handle = event->evt.gap_evt.conn_handle;
            break;

        case BLE_GAP_EVT_DISCONNECTED:
            m_conn_handle = BLE_CONN_HANDLE_INVALID;
            m_status_notifications = false;
            m_disconnect_pending = false;
            if (m_shutdown_after_disconnect) {
                m_shutdown_after_disconnect = false;
                m_system_off_pending = true;
                break;
            }
            m_state = STATE_IDLE;
            m_error = ERROR_NONE;
            m_expected_offset = 0;
            m_running_crc = 0xFFFFFFFFu;
            m_abort_pending = true;
            advertising_start();
            break;

        case BLE_GAP_EVT_ADV_SET_TERMINATED:
            if (m_conn_handle == BLE_CONN_HANDLE_INVALID) {
                m_system_off_pending = true;
            }
            break;

        case BLE_GATTS_EVT_WRITE: {
            ble_gatts_evt_write_t const *write = &event->evt.gatts_evt.params.write;
            if (write->handle == m_control_handles.value_handle) {
                handle_control(write->data, write->len);
            } else if (write->handle == m_data_handles.value_handle) {
                handle_data(write->data, write->len);
            } else if (write->handle == m_status_handles.cccd_handle && write->len == 2u) {
                m_status_notifications = ble_srv_is_notification_enabled(write->data);
                status_publish();
            }
            break;
        }

        case BLE_GAP_EVT_SEC_PARAMS_REQUEST:
            APP_ERROR_CHECK(sd_ble_gap_sec_params_reply(m_conn_handle,
                                                         BLE_GAP_SEC_STATUS_PAIRING_NOT_SUPP,
                                                         NULL,
                                                         NULL));
            break;

        case BLE_GAP_EVT_PHY_UPDATE_REQUEST: {
            ble_gap_phys_t preferred_phys = {
                .tx_phys = BLE_GAP_PHY_AUTO,
                .rx_phys = BLE_GAP_PHY_AUTO
            };
            APP_ERROR_CHECK(sd_ble_gap_phy_update(m_conn_handle, &preferred_phys));
            break;
        }

        case BLE_GATTS_EVT_SYS_ATTR_MISSING:
            APP_ERROR_CHECK(sd_ble_gatts_sys_attr_set(m_conn_handle, NULL, 0, 0));
            break;

        case BLE_GATTS_EVT_HVN_TX_COMPLETE:
            if (m_disconnect_pending && m_conn_handle != BLE_CONN_HANDLE_INVALID) {
                m_disconnect_pending = false;
                m_shutdown_after_disconnect = true;
                APP_ERROR_CHECK(sd_ble_gap_disconnect(
                    m_conn_handle,
                    BLE_HCI_REMOTE_USER_TERMINATED_CONNECTION));
            }
            break;

        case BLE_GATTC_EVT_TIMEOUT:
        case BLE_GATTS_EVT_TIMEOUT:
            APP_ERROR_CHECK(sd_ble_gap_disconnect(m_conn_handle,
                                                   BLE_HCI_REMOTE_USER_TERMINATED_CONNECTION));
            break;

        default:
            break;
    }
}

NRF_SDH_BLE_OBSERVER(m_ble_observer, APP_BLE_OBSERVER_PRIO, ble_evt_handler, NULL);

void assert_nrf_callback(uint16_t line_num, uint8_t const *file_name) {
    app_error_handler(0xDEADBEEFu, line_num, file_name);
}

int main(void) {
    nrf_gpio_cfg_input(WAKE_BUTTON_1, NRF_GPIO_PIN_PULLUP);
    nrf_gpio_cfg_input(WAKE_BUTTON_2, NRF_GPIO_PIN_PULLUP);

    APP_ERROR_CHECK(nrf_sdh_enable_request());
    uint32_t ram_start = 0;
    APP_ERROR_CHECK(nrf_sdh_ble_default_cfg_set(APP_BLE_CONN_CFG_TAG, &ram_start));
    APP_ERROR_CHECK(nrf_sdh_ble_enable(&ram_start));

    gap_init();
    services_init();
    advertising_init();
    advertising_start();

    for (;;) {
        if (m_abort_pending) {
            m_abort_pending = false;
            panel_abort_stream();
        }
        if (m_prepare_pending) {
            m_prepare_pending = false;
            if (panel_begin_stream()) {
                m_state = STATE_RECEIVING;
                status_publish();
            } else {
                fail(ERROR_PANEL_TIMEOUT);
            }
        }
        if (m_finish_pending) {
            m_finish_pending = false;
            if (panel_finish_stream()) {
                m_state = STATE_COMPLETE;
                status_publish();
                if (m_conn_handle != BLE_CONN_HANDLE_INVALID) {
                    if (m_status_notifications) {
                        m_disconnect_pending = true;
                    } else {
                        m_shutdown_after_disconnect = true;
                        APP_ERROR_CHECK(sd_ble_gap_disconnect(
                            m_conn_handle,
                            BLE_HCI_REMOTE_USER_TERMINATED_CONNECTION));
                    }
                } else {
                    m_system_off_pending = true;
                }
            } else {
                fail(ERROR_PANEL_TIMEOUT);
            }
        }
        if (m_system_off_pending) {
            m_system_off_pending = false;
            enter_system_off();
        }
        APP_ERROR_CHECK(sd_app_evt_wait());
    }
}
