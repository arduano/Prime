Texture2D ShaderTexture : register(t0);
SamplerState Sampler : register(s0);

struct VS_IN
{
	float4 pos : POSITION;
	float2 uv : UV;
	float min : MIN;
	float max : MAX;
	float pow : POW;
};

struct PS_IN
{
	float4 pos : SV_POSITION;
	float2 uv : UV;
	float min : MIN;
	float max : MAX;
	float pow : POW;
};

PS_IN VS(VS_IN input)
{
	//PS_IN p = (PS_IN)0;
	//p.pos = input.pos;
	//p.uv = input.uv;
	//return p;
	PS_IN o = (PS_IN)input;
	//o.uv = o.pos.xy;
	return o;
}

float keyify(float freq) {
	return log(freq / 440) / log(2) * 12 + 69;
}

float freqency(float key) {
	return pow(2, (key - 69 - 7 * 3) / 12) * 440;
}

float4 PS(PS_IN input) : SV_Target
{
	//input.uv.y = freqency(input.uv.y * 127);
	//input.uv.y = input.uv.y / 8192 * (48000 / 8192);
	//input.uv.y /= 2;
	float col = ShaderTexture.Sample(Sampler, input.uv);
	col -= input.min;
	col /= (input.max - input.min);
	if (col < 0) return float4(0, 0, 1, 1);
	col = pow(col, input.pow);
	return float4(0, col, 1, 1);
}