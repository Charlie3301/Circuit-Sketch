/* Adafruit_MPU6050.h -- o acelerometro e giroscopio MPU-6050 (a da Adafruit).
 * O getEvent() pede ao simulador os tres eventos e copia os campos. */
#ifndef _ADAFRUIT_MPU6050_H
#define _ADAFRUIT_MPU6050_H

#include "Adafruit_Sensor.h"
#include "cs_objeto.h"
#include "Wire.h"

#define MPU6050_I2CADDR_DEFAULT 0x68

typedef enum { MPU6050_RANGE_2_G = 0, MPU6050_RANGE_4_G, MPU6050_RANGE_8_G, MPU6050_RANGE_16_G } mpu6050_accel_range_t;
typedef enum { MPU6050_RANGE_250_DEG = 0, MPU6050_RANGE_500_DEG, MPU6050_RANGE_1000_DEG, MPU6050_RANGE_2000_DEG } mpu6050_gyro_range_t;
typedef enum {
    MPU6050_BAND_260_HZ = 0,
    MPU6050_BAND_184_HZ,
    MPU6050_BAND_94_HZ,
    MPU6050_BAND_44_HZ,
    MPU6050_BAND_21_HZ,
    MPU6050_BAND_10_HZ,
    MPU6050_BAND_5_HZ,
} mpu6050_bandwidth_t;
typedef enum {
    MPU6050_HIGHPASS_DISABLE = 0,
    MPU6050_HIGHPASS_5_HZ,
    MPU6050_HIGHPASS_2_5_HZ,
    MPU6050_HIGHPASS_1_25_HZ,
    MPU6050_HIGHPASS_0_63_HZ,
    MPU6050_HIGHPASS_UNUSED,
    MPU6050_HIGHPASS_HOLD,
} mpu6050_highpass_t;
typedef enum { MPU6050_CYCLE_1_25_HZ = 0, MPU6050_CYCLE_5_HZ, MPU6050_CYCLE_20_HZ, MPU6050_CYCLE_40_HZ } mpu6050_cycle_rate_t;

class Adafruit_MPU6050 : public CsObjeto {
    char eventos[3][32];

    void ler(const char *nome, sensors_event_t *e, int tipo)
    {
        if (!e) return;
        memset(e, 0, sizeof(*e));
        cs_val a[] = {cs_t(nome), cs_t("acceleration.x")};
        e->acceleration.x = (float)cs_real(nullptr, "__ler", 2, a);
        a[1] = cs_t("acceleration.y");
        e->acceleration.y = (float)cs_real(nullptr, "__ler", 2, a);
        a[1] = cs_t("acceleration.z");
        e->acceleration.z = (float)cs_real(nullptr, "__ler", 2, a);
        e->type = tipo;
        e->timestamp = (int32_t)millis();
        e->version = (int32_t)sizeof(sensors_event_t);
        if (tipo == SENSOR_TYPE_AMBIENT_TEMPERATURE) {
            a[1] = cs_t("temperature");
            e->temperature = (float)cs_real(nullptr, "__ler", 2, a);
        }
    }

public:
    Adafruit_MPU6050()
    {
        cs_criar("Adafruit_MPU6050");
        for (auto &e : eventos) {
            cs_val v[] = {cs_t("sensors_event_t")};
            String nome = cs_texto(nullptr, "__criar", 1, v);
            snprintf(e, sizeof(e), "%s", nome.c_str());
        }
    }
    bool begin(uint8_t i2c_addr = MPU6050_I2CADDR_DEFAULT, TwoWire *wire = &Wire, int32_t sensorID = 0)
    {
        (void)sensorID;
        return cs_obj_i(cs_nome, "begin", i2c_addr, wire) != 0;
    }
    bool getEvent(sensors_event_t *accel, sensors_event_t *gyro, sensors_event_t *temp)
    {
        cs_obj_n(cs_nome, "getEvent", cs_nome_de{eventos[0]}, cs_nome_de{eventos[1]}, cs_nome_de{eventos[2]});
        ler(eventos[0], accel, SENSOR_TYPE_ACCELEROMETER);
        ler(eventos[1], gyro, SENSOR_TYPE_GYROSCOPE);
        ler(eventos[2], temp, SENSOR_TYPE_AMBIENT_TEMPERATURE);
        return true;
    }
    CS_M_NADA(setAccelerometerRange)
    CS_M_NADA(setGyroRange)
    CS_M_NADA(setFilterBandwidth)
    CS_M_NADA(setHighPassFilter)
    CS_M_NADA(setMotionDetectionThreshold)
    CS_M_NADA(setMotionDetectionDuration)
    CS_M_NADA(setInterruptPinLatch)
    CS_M_NADA(setInterruptPinPolarity)
    CS_M_NADA(setMotionInterrupt)
    CS_M_NADA(setSampleRateDivisor)
    CS_M_NADA(setCycleRate)
    CS_M_NADA(enableSleep)
    CS_M_NADA(enableCycle)
    CS_M_NADA(reset)
    CS_M_NADA(setClock)
    CS_M_NADA(setTemperatureStandby)
    mpu6050_accel_range_t getAccelerometerRange() { return (mpu6050_accel_range_t)cs_obj_i(cs_nome, "getAccelerometerRange"); }
    mpu6050_gyro_range_t getGyroRange() { return (mpu6050_gyro_range_t)cs_obj_i(cs_nome, "getGyroRange"); }
    mpu6050_bandwidth_t getFilterBandwidth() { return (mpu6050_bandwidth_t)cs_obj_i(cs_nome, "getFilterBandwidth"); }
    bool getMotionInterruptStatus() { return cs_obj_i(cs_nome, "getMotionInterruptStatus") != 0; }
};

#endif
