EXTENSION = decrypt_and_compare_c
MODULE_big = decrypt_and_compare_c
OBJS = decrypt_and_compare.o
DATA = decrypt_and_compare_c--1.0.sql
SHLIB_LINK = -lssl -lcrypto

PG_CONFIG = pg_config
PGXS := $(shell $(PG_CONFIG) --pgxs)
include $(PGXS)
