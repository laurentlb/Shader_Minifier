// Shader by Fizzer / Eos
// Copied from https://github.com/demoscene-source-archive/terrarium/blob/master/src/shaders/fragment.glsl
// https://www.pouet.net/prod.php?which=82417

#version 430
layout(location=0)uniform int m;
//[
layout(local_size_x=8,local_size_y=8)in;
//]
uniform layout(binding=0,r32ui) coherent restrict uimage2D d0;
uniform layout(binding=1,rgba8) coherent writeonly restrict image2D outtex;
uniform layout(binding=0) sampler2D lt;
float time=float(m)/44100.;

int IH(int a){
	a=(a^61)^(a>>16);
	a=a+(a<<3);
	a=a^(a>>4);
	a=a*0x27d4eb2d;
	a=a^(a>>15);
	return a;
}

float H(int a){
	a=(a^61)^(a>>16);
	a=a+(a<<3);
	a=a^(a>>4);
	a=a*0x27d4eb2d;
	a=a^(a>>15);
	return float(a)/0x7FFFFFFF;
}

vec2 rand2(int a){
    return vec2(H(a^0x348593),
                H(a^0x8593D5));
}

vec2 randn(vec2 randuniform){
    vec2 r = randuniform;
    r.x = sqrt(-2.*log(1e-9+abs(r.x)));
    r.y *= 6.28318;
    r = r.x*vec2(cos(r.y),sin(r.y));
    return r;
}
vec2 randc(vec2 randuniform){
    vec2 r=randuniform;
    r.x=sqrt(r.x);
    r.y*=6.28318;
    r=r.x*vec2(cos(r.y),sin(r.y));
    return r;
}

vec3 S(vec3 x){
    return vec3(x/(1e-6+dot(x.xyz,x.xyz)));
}

void main(){

   ivec2 coord=ivec2(gl_GlobalInvocationID.xy);
   vec3 p=vec3(0);

	if(m<0)
	{
	   if(coord.x<1280&&coord.y<720)
	   {
		  uint x=imageLoad(d0,coord).x;
		  vec3 sc=mix(vec3(1),vec3(0,.35,.9),smoothstep(0.,23.,-time));
		  vec3 ec=mix(vec3(1,1.3,.28),vec3(.9,.2,.3),smoothstep(100.,138.,-time));
		  p=mix(sc,ec,sqrt(clamp(float(x>>20)/1024,0,1)))*(x&0xfffff)/100; 
		  p*=smoothstep(0,14,-time);
		  vec2 q=abs(vec2(coord)/vec2(1280,720)-.5);
		  p*=.8+.2*cos(time*20.)*max(0.,1.+time/50.);
		  p=pow(p,vec3(1.3,1.1,1).bgr)*(1-(pow(q.x,4)+pow(q.y,4))*4);
		  //p=mix(p,vec3(16),texelFetch(lt,coord,0).r);
		  p=sqrt(p/(p+1));
		  float a=smoothstep(125.,130.,-time)-smoothstep(136.,145.,-time);
		  p=mix(p,vec3(1),a*textureLod(lt,clamp((vec2(coord)/vec2(1280,720)-vec2(.05))*2.9,-.01,.99),1.).r);
		  p=mix(p,vec3(1),a*textureLod(lt,clamp((vec2(coord)/vec2(1280,720)-vec2(.05))*5.,-.01,.99),2.).g);
		  p=mix(p,vec3(1),a*textureLod(lt,clamp((vec2(coord)/vec2(1280,720)-vec2(.05))*9.,-.01,.99),3.).b);
		  imageStore(outtex,coord,vec4(p,1));
		  imageStore(d0,coord,uvec4(0));
	   }
		return;
	}

	float col=0.;
   time+=H(coord.x+coord.y*720+m*10000-1)/24.;
   for(int i=0;i<256;++i)
   {
		int seed=coord.x+coord.y*720+m*10000+i*100000000;

		float r = H(seed=IH(seed))*1.08;
		
		if(H(seed=IH(seed))<.02+smoothstep(0.,25.,abs(time-90.)))
		{
				
			if(r<.15)
			{
				p*=vec3(.1,.2,.1);
			}
			else if(r<.66)
			{
				p+=vec3(0,-1-cos(time/20.)*.5,0);
				p*=vec3(.9,.8,.9)*(1.5+sin(time/10.));
				r=3.1415926*144./180.;
				p.xz=mat2(cos(r),sin(r),-sin(r),cos(r))*p.xz;
				p+=vec3(0,1.,0);
			}
			else if(r<1.)
			{
				r=3.1415926*40./180.;
				p.xy=mat2(cos(r),sin(r),-sin(r),cos(r))*p.xy;
				p*=.7;
				p+=vec3(0,.6,0);
			}
			else if(r<1.01)
			{
				col = mix(col,1.,.99);
				p.xy += randn(rand2(seed))*0.1;
				r=.25*dot(p.xy,p.xy)+1;
				p = (vec3(p.xy/r,2./(r)-1))*(0.068)/4.+vec3(randn(rand2(IH(seed)))*0.001,0)+vec3(0,0,1.497);
			}
			else if(r<1.04)
			{
			   col = mix(col,1.,0.5);
				p.xy += randn(rand2(seed))*2.;
				p = vec3(sin(p.x),p.y,cos(p.x))*(1.3+cos(time*4.)*.8)+vec3(0,0,length(p.xy))*2.651 + vec3(0,0,1.702);
			}
			else if(r<1.06)
			{
				col = mix(col,0.,0.2);
				p.xy = p.xy + vec2(1, 0);
				p=S(p)*1.401;
			}
			else if(r<1.07)
			{
				col = mix(col,0.,0.2);
				p.xy = p.xy + vec2(-0.5, -0.866025);
				p=S(p);
			}
			else
			{
				col = mix(col,0.,0.2);
				col=1.;
				p.xy = p.xy + vec2(-0.5, 0.866025);
				p=S(p);
				p.z+=.1;
			}

			if(time>48.)
				p.z=mix(p.z,mod(p.z+time/2.+2,4.)-2,smoothstep(48.,49.,time));

		}else{
			
			float s = H(seed=IH(seed))*(0.93+0.02+0.03+0.02);

			p.y-=.8;
			
			if(s<.93)
				p.xy=(mat3(.860671,-0.402177,0,.401487,.860992,0,.108537,.075138,1)*vec3(p.xy,1)).xy;
			else if(s<(0.93+0.02))
				p.xy=(mat3(.094957,.237023,0,-0.000995,.002036,0,-0.746911,.047343,1)*vec3(p.xy,1)).xy;
			else if(s<(0.93+0.02+0.03))
				p.xy=(mat3(.150288,0,0,0,.146854,0,-0.563199,.032007,1)*vec3(p.xy,1)).xy;
			else{
				p.xy=(mat3(.324279,.005846,0,-0.002163,.001348,0,-0.557936,-0.139735,1)*vec3(p.xy,1)).xy;
			}
			
			p.y+=.8;
			
		
		}

		p*=1.+max(0.,time-130)/6.;

		vec3 q=p.zxy,x,w=vec3(H(seed=IH(seed)),H(seed=IH(seed)),H(seed=IH(seed)));

q.z-=.7;

		float phi=w.x*3.141592*2,u=w.y*2-1;
		u=sqrt(1-u*u);
		x=vec3(cos(phi)*u,sin(phi)*u,w.y*2-1)*pow(w.z,1./3)*3;

		q=mix(q,q+x*.5,.1*pow(H(seed),16.));
		q=q+x*.2*(1.-min(time/30.,1.)+max(0.,time-130)/20.); // defocus

		u=time/8.+199.*step(.5,H(int(floor(time/60.*64.))))*step(60.,time)*step(time,120);
//		u=time/8.+floor(time/60.*64.)*step(60.,time)*step(time,120);
		q.xy=mat2(cos(u),sin(u),-sin(u),cos(u))*q.xy;
		u=.4+time/9.;
		q.yz=mat2(cos(u),sin(u),-sin(u),cos(u))*q.yz;
		q.xz*=1.+H(seed=IH(seed))*max(0.,time-120)/20.; // zoomblur
		q.xz=400*q.xz+vec2(640,360)+randc(rand2(seed))*q.y*15.;
		
		if(q.x>0&&q.z>0&&q.x<1280&&q.z<720)
			imageAtomicAdd(d0,ivec2(q.x,q.z),1|(int(col>.5?1:0)<<20));
	  
   }
}
