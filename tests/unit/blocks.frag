float test_if()
{
  int foo = 2;
  float bar;
  if (foo == 2)
    foo++;
  if (foo < 5)
    if (foo == 3)
      bar = 0.2;
    else
      bar = 0.3;
  else
    bar = 0.4;
  return bar;
}

int k = 5;

float test_for()
{
  int foo = 2;
  int n = 0;
  for (int i = 0; i < 4; i++)
    foo += i;
  for (foo++; n < 4 ; n++)
    {
      int k = n - 1;
      foo += k;
    }
  return 1. / float(k);
}

int test_block()
{
  {}
  {if (k == 0) {} else {}}
  for (int i = 0; i < 2; i++)
  {
    if (k == 1) {k++; return 2;} else {break;}
  }
}

void main()
{
  float a = test_if();
  float b = test_for();
  gl_FragColor=vec4(.2,a,b,0.);
}
