#ifndef mystdbool_h_
#define mystdbool_h_

#if defined(_MSC_VER)
typedef int bool;
enum {
	true = 1,
	false = 0,
};
#else
// must be GNU C, then.
#include <stdbool.h>
#endif

#endif /* mystdbool_h_ */