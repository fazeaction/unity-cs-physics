#include "UnityCG.cginc"

// Hash function from H. Schechter & R. Bridson, goo.gl/RXiKaH
uint Hash(uint s)
{
    s ^= 2747636419u;
    s *= 2654435769u;
    s ^= s >> 16;
    s *= 2654435769u;
    s ^= s >> 16;
    s *= 2654435769u;
    return s;
}

float Random(uint seed)
{
    return float(Hash(seed)) / 4294967295.0; // 2^32-1
}

// Rotate a vector by a quaternion
float3 float3_applyQuat(float3 v, float4 q) {
	float ix =  q.w * v.x + q.y * v.z - q.z * v.y; // TODO: vectorize
	float iy =  q.w * v.y + q.z * v.x - q.x * v.z;
	float iz =  q.w * v.z + q.x * v.y - q.y * v.x;
	float iw = -q.x * v.x - q.y * v.y - q.z * v.z;

	return float3(
		ix * q.w + iw * -q.x + iy * -q.z - iz * -q.y,
		iy * q.w + iw * -q.y + iz * -q.x - ix * -q.z,
		iz * q.w + iw * -q.z + ix * -q.y - iy * -q.x
	);
}

int _gridIndex(float3 particlePos, float3 gridPos, float3 cellSize,float3 gridRes) {
	int3 gridLocation = (particlePos - gridPos) / cellSize;
	return gridLocation.x + gridRes.x * gridLocation.y + (gridRes.x * gridRes.y * gridLocation.z);
}

float3 worldPosToGridPos(float3 particlePos, float3 gridPos, float3 cellSize){
	return floor((particlePos - gridPos)/(cellSize*1.0));
}

// Convert grid position to UV coord in the grid texture
uint gridPosToGridBuffer(float3 gridPoss, int subIndex, float3 gridRes){
	gridPoss = clamp(gridPoss, float3(0.0,0.0,0.0), gridRes-float3(0.0,0.0,0.0)); // Keep within limits
	return floor(gridPoss.x + (gridRes.x * gridPoss.y) + (gridRes.x * gridRes.y * gridPoss.z));
}

float4 quat_integrate(float4 q, float3 w, float dt){
	float half_dt = dt * 0.5;
	q.x += half_dt * (w.x * q.w + w.y * q.z - w.z * q.y); // TODO: vectorize
	q.y += half_dt * (w.y * q.w + w.z * q.x - w.x * q.z);
	q.z += half_dt * (w.z * q.w + w.x * q.y - w.y * q.x);
	q.w += half_dt * (- w.x * q.x - w.y * q.y - w.z * q.z);

	return normalize(q);
}

float3x3 quat2mat(float4 q){
	float x = q.x;
	float y = q.y;
	float z = q.z;
	float w = q.w;

	float x2 = x + x;
	float y2 = y + y;
	float z2 = z + z;

	float xx = x * x2;
	float xy = x * y2;
	float xz = x * z2;
	float yy = y * y2;
	float yz = y * z2;
	float zz = z * z2;
	float wx = w * x2;
	float wy = w * y2;
	float wz = w * z2;

	return float3x3(
		float3(1.0 - ( yy + zz ),  xy - wz,            xz + wy),
		float3(xy + wz,            1.0 - ( xx + zz ),  yz - wx),
		float3(xz - wy,            yz + wx,            1.0 - ( xx + yy ))
	);
}

float3x3 transpose2( float3x3 v ) {
	float3 row0 = float3(v[0].x, v[1].x, v[2].x);
	float3 row1 = float3(v[0].y, v[1].y, v[2].y);
	float3 row2 = float3(v[0].z, v[1].z, v[2].z);
	return float3x3(row0,row1,row2);
}

float3x3 invInertiaWorld(float4 q, float3 invInertia){
	float3x3 R = quat2mat(q);
	float3x3 I = float3x3(
		float3(invInertia.x, 0, 0),
		float3(0, invInertia.y, 0),
		float3(0, 0, invInertia.z)
	);
	return transpose2(R) * I * R;
}