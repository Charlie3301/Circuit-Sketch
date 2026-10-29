/* MPU6050.h -- o MPU-6050 pela biblioteca do Jeff Rowberg (I2Cdevlib /
 * Electronic Cats): os valores crus (16 bits) de cada eixo. */
#ifndef _MPU6050_H_
#define _MPU6050_H_

#include "cs_objeto.h"
#include "Wire.h"

#define MPU6050_ADDRESS_AD0_LOW 0x68
#define MPU6050_ADDRESS_AD0_HIGH 0x69
#define MPU6050_DEFAULT_ADDRESS MPU6050_ADDRESS_AD0_LOW

class MPU6050 : public CsObjeto {
public:
    MPU6050(uint8_t address = MPU6050_DEFAULT_ADDRESS) { cs_criar("MPU6050", address); }
    void initialize() { cs_obj_n(cs_nome, "initialize"); }
    bool testConnection() { return cs_obj_i(cs_nome, "testConnection") != 0; }
    int16_t getAccelerationX() { return (int16_t)cs_obj_i(cs_nome, "getAccelerationX"); }
    int16_t getAccelerationY() { return (int16_t)cs_obj_i(cs_nome, "getAccelerationY"); }
    int16_t getAccelerationZ() { return (int16_t)cs_obj_i(cs_nome, "getAccelerationZ"); }
    int16_t getRotationX() { return (int16_t)cs_obj_i(cs_nome, "getRotationX"); }
    int16_t getRotationY() { return (int16_t)cs_obj_i(cs_nome, "getRotationY"); }
    int16_t getRotationZ() { return (int16_t)cs_obj_i(cs_nome, "getRotationZ"); }
    int16_t getTemperature() { return (int16_t)lround((cs_obj_r(cs_nome, "readTemperature") - 36.53) * 340.0); }
    void getAcceleration(int16_t *x, int16_t *y, int16_t *z)
    {
        *x = getAccelerationX();
        *y = getAccelerationY();
        *z = getAccelerationZ();
    }
    void getRotation(int16_t *x, int16_t *y, int16_t *z)
    {
        *x = getRotationX();
        *y = getRotationY();
        *z = getRotationZ();
    }
    void getMotion6(int16_t *ax, int16_t *ay, int16_t *az, int16_t *gx, int16_t *gy, int16_t *gz)
    {
        getAcceleration(ax, ay, az);
        getRotation(gx, gy, gz);
    }
    void setFullScaleAccelRange(uint8_t) {}
    void setFullScaleGyroRange(uint8_t) {}
    void setXAccelOffset(int16_t) {}
    void setYAccelOffset(int16_t) {}
    void setZAccelOffset(int16_t) {}
    void setXGyroOffset(int16_t) {}
    void setYGyroOffset(int16_t) {}
    void setZGyroOffset(int16_t) {}
    void CalibrateAccel(uint8_t = 15) {}
    void CalibrateGyro(uint8_t = 15) {}
    void setSleepEnabled(bool) {}
};

#endif
