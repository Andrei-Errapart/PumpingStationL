

MBPROXY		:= mbproxy
MBPROXY_SRC	:= mbproxy.cpp DigitalIo.cpp AnalogInput.cpp modbus_datastore.cpp PError.cpp Led.cpp
MBPROXY_CSRC	:= modbus_slave.c
MBPROXY_OBJ	:= $(MBPROXY_SRC:.cpp=.o) $(MBPROXY_CSRC:.c=.o)


CC	      	:= arm-angstrom-linux-gnueabi-gcc
CXX		:= arm-angstrom-linux-gnueabi-g++
STRIP		:= arm-angstrom-linux-gnueabi-strip
CFLAGS	  	:= -Wall -g -I. -DHAVE_OS=1 #-DGPIO_DIR="\"gpio\""
LINKFLAGS       := -Wall -g 



# all dependency files.
DEP	     := $(MBPROXY_OBJ:.o=.d)

all:	    $(MBPROXY)

%.o:	    %.cpp
		$(CXX) $(CFLAGS) -o $@ -c $<

%.o:	    %.c
		$(CC) $(CFLAGS) -o $@ -c $<

$(MBPROXY):	 $(MBPROXY_OBJ)
		$(CXX) $^ $(LINKFLAGS) -o $@
		$(STRIP) $(MBPROXY)

clean:
		rm -f $(MBPROXY_OBJ)
		rm -f $(DEP)
		rm -f $(MBPROXY)

# dryrun on the local filesystem.
DRYRUN		:= dryrun
DRYRUN_OBJ	:= $(MBPROXY_OBJ:.o=.dr.o)
DRYRUN_FLAGS	:= -DGPIO_DIR="\"gpio\"" -DADC_DIR="\"adc\"" -DLED_DIR="\"leds\""

%.dr.o:		%.cpp
		g++ $(CFLAGS) $(DRYRUN_FLAGS) -o $@ -c $<

%.dr.o:		%.c
		gcc $(CFLAGS) $(DRYRUN_FLAGS) -o $@ -c $<

$(DRYRUN):	$(DRYRUN_OBJ)
		g++ $^ $(LINKFLAGS) -o $@

# dependency autogeneration magic.
include $(DEP)

%.d:	    %.cpp
		$(CXX) -MM -MG $(CFLAGS) $*.cpp | sed -e 's@^\(.*\)\.o:@\1.d \1.o:@' > $@

%.d:	    %.c
		$(CC) -MM -MG $(CFLAGS)  $*.c   | sed -e 's@^\(.*\)\.o:@\1.d \1.o:@' > $@

