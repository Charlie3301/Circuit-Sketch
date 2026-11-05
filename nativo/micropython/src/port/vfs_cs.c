/* vfs_cs.c -- os arquivos do MicroPython nos do simulador: a flash e o cartao da bancada. Na placa
 * seria o LittleFS ou o FAT (extmod/vfs_lfs.c) sobre um bloco; aqui os arquivos ja vem prontos. */
#include <stdio.h>
#include <string.h>

#include "py/mperrno.h"
#include "py/runtime.h"
#include "py/stream.h"
#include "extmod/vfs.h"
#include "port/cs_py_ponte.h"

const mp_obj_type_t cs_vfs_type;
const mp_obj_type_t cs_vfs_textio_type;
const mp_obj_type_t cs_vfs_fileio_type;

typedef struct {
    mp_obj_base_t base;
    void *arquivo;
} arquivo_obj_t;

typedef struct {
    mp_obj_base_t base;
} vfs_obj_t;

/* O VFS ja tirou o ponto de montagem: "/x" no flash, e "/sd" e outro. */
typedef struct {
    mp_obj_base_t base;
    const char *prefixo;
} vfs_cs_obj_t;

static const char *caminhoDe(mp_obj_t self_in, mp_obj_t caminho, char *buf, size_t max)
{
    vfs_cs_obj_t *self = MP_OBJ_TO_PTR(self_in);
    const char *c = mp_obj_str_get_str(caminho);
    snprintf(buf, max, "%s%s%s", self->prefixo, c[0] == '/' ? "" : "/", c);
    size_t n = strlen(buf);
    if (n > 1 && buf[n - 1] == '/') buf[n - 1] = 0;
    return buf;
}

/* ---- o arquivo aberto ---- */
static mp_uint_t arquivo_ler(mp_obj_t o_in, void *buf, mp_uint_t size, int *errcode)
{
    arquivo_obj_t *o = MP_OBJ_TO_PTR(o_in);
    if (!o->arquivo) {
        *errcode = MP_EBADF;
        return MP_STREAM_ERROR;
    }
    int n = cs_py_arquivo_ler(o->arquivo, buf, size);
    return n < 0 ? 0 : n;
}

static mp_uint_t arquivo_escrever(mp_obj_t o_in, const void *buf, mp_uint_t size, int *errcode)
{
    arquivo_obj_t *o = MP_OBJ_TO_PTR(o_in);
    if (!o->arquivo) {
        *errcode = MP_EBADF;
        return MP_STREAM_ERROR;
    }
    return cs_py_arquivo_escrever(o->arquivo, buf, size);
}

static mp_uint_t arquivo_ioctl(mp_obj_t o_in, mp_uint_t request, uintptr_t arg, int *errcode)
{
    arquivo_obj_t *o = MP_OBJ_TO_PTR(o_in);
    if (request == MP_STREAM_CLOSE) {
        if (o->arquivo) cs_py_arquivo_fechar(o->arquivo);
        o->arquivo = NULL;
        return 0;
    }
    if (!o->arquivo) {
        *errcode = MP_EBADF;
        return MP_STREAM_ERROR;
    }
    switch (request) {
    case MP_STREAM_SEEK: {
        struct mp_stream_seek_t *s = (struct mp_stream_seek_t *)arg;
        if (!cs_py_arquivo_ir(o->arquivo, s->offset, s->whence)) {
            *errcode = MP_EINVAL;
            return MP_STREAM_ERROR;
        }
        s->offset = cs_py_arquivo_posicao(o->arquivo);
        return 0;
    }
    case MP_STREAM_FLUSH:
        cs_py_arquivo_salvar(o->arquivo);
        return 0;
    case MP_STREAM_POLL:
        return arg & (MP_STREAM_POLL_RD | MP_STREAM_POLL_WR);
    }
    *errcode = MP_EINVAL;
    return MP_STREAM_ERROR;
}

static void arquivo_print(const mp_print_t *print, mp_obj_t self, mp_print_kind_t kind)
{
    mp_printf(print, "<io.%s>", mp_obj_get_type_str(self));
}

static const mp_rom_map_elem_t arquivo_locals_dict_table[] = {
    { MP_ROM_QSTR(MP_QSTR_read), MP_ROM_PTR(&mp_stream_read_obj) },
    { MP_ROM_QSTR(MP_QSTR_readinto), MP_ROM_PTR(&mp_stream_readinto_obj) },
    { MP_ROM_QSTR(MP_QSTR_readline), MP_ROM_PTR(&mp_stream_unbuffered_readline_obj) },
    { MP_ROM_QSTR(MP_QSTR_readlines), MP_ROM_PTR(&mp_stream_unbuffered_readlines_obj) },
    { MP_ROM_QSTR(MP_QSTR_write), MP_ROM_PTR(&mp_stream_write_obj) },
    { MP_ROM_QSTR(MP_QSTR_close), MP_ROM_PTR(&mp_stream_close_obj) },
    { MP_ROM_QSTR(MP_QSTR_seek), MP_ROM_PTR(&mp_stream_seek_obj) },
    { MP_ROM_QSTR(MP_QSTR_tell), MP_ROM_PTR(&mp_stream_tell_obj) },
    { MP_ROM_QSTR(MP_QSTR_flush), MP_ROM_PTR(&mp_stream_flush_obj) },
    { MP_ROM_QSTR(MP_QSTR___del__), MP_ROM_PTR(&mp_stream_close_obj) },
    { MP_ROM_QSTR(MP_QSTR___enter__), MP_ROM_PTR(&mp_identity_obj) },
    { MP_ROM_QSTR(MP_QSTR___exit__), MP_ROM_PTR(&mp_stream___exit___obj) },
};
static MP_DEFINE_CONST_DICT(arquivo_locals_dict, arquivo_locals_dict_table);

static const mp_stream_p_t fileio_stream_p = {
    .read = arquivo_ler,
    .write = arquivo_escrever,
    .ioctl = arquivo_ioctl,
};
static const mp_stream_p_t textio_stream_p = {
    .read = arquivo_ler,
    .write = arquivo_escrever,
    .ioctl = arquivo_ioctl,
    .is_text = true,
};

MP_DEFINE_CONST_OBJ_TYPE(
    cs_vfs_fileio_type,
    MP_QSTR_FileIO,
    MP_TYPE_FLAG_ITER_IS_STREAM,
    print, arquivo_print,
    protocol, &fileio_stream_p,
    locals_dict, &arquivo_locals_dict
    );
MP_DEFINE_CONST_OBJ_TYPE(
    cs_vfs_textio_type,
    MP_QSTR_TextIOWrapper,
    MP_TYPE_FLAG_ITER_IS_STREAM,
    print, arquivo_print,
    protocol, &textio_stream_p,
    locals_dict, &arquivo_locals_dict
    );

/* ---- o sistema de arquivos ---- */
static mp_obj_t vfs_cs_mount(mp_obj_t self_in, mp_obj_t readonly, mp_obj_t mkfs)
{
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_3(vfs_cs_mount_obj, vfs_cs_mount);

static mp_obj_t vfs_cs_umount(mp_obj_t self_in)
{
    vfs_cs_obj_t *self = MP_OBJ_TO_PTR(self_in);
    if (self->prefixo[0]) cs_py_cartao_desmontar();
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_1(vfs_cs_umount_obj, vfs_cs_umount);

static mp_obj_t vfs_cs_open(mp_obj_t self_in, mp_obj_t caminho, mp_obj_t modo_in)
{
    char buf[160];
    const char *modo = mp_obj_str_get_str(modo_in);
    const mp_obj_type_t *tipo = &cs_vfs_textio_type;
    char m[4] = "r";
    for (const char *p = modo; *p; p++) {
        switch (*p) {
        case 'r': case 'w': case 'a': m[0] = *p; break;
        case '+': m[1] = '+'; m[2] = 0; break;
        case 'b': tipo = &cs_vfs_fileio_type; break;
        case 't': tipo = &cs_vfs_textio_type; break;
        default: break;
        }
    }
    const char *c = caminhoDe(self_in, caminho, buf, sizeof(buf));
    if (m[0] == 'r' && !cs_py_arq_existe(c)) mp_raise_OSError(MP_ENOENT);
    if (cs_py_arq_existe(c) && cs_py_arq_eh_pasta(c)) mp_raise_OSError(MP_EISDIR);
    void *a = cs_py_arquivo_abrir(c, m);
    if (!a) mp_raise_OSError(MP_ENOENT);
    arquivo_obj_t *o = mp_obj_malloc_with_finaliser(arquivo_obj_t, tipo);
    o->arquivo = a;
    return MP_OBJ_FROM_PTR(o);
}
static MP_DEFINE_CONST_FUN_OBJ_3(vfs_cs_open_obj, vfs_cs_open);

static mp_obj_t vfs_cs_chdir(mp_obj_t self_in, mp_obj_t caminho)
{
    char buf[160];
    const char *c = caminhoDe(self_in, caminho, buf, sizeof(buf));
    if (!cs_py_arq_eh_pasta(c)) mp_raise_OSError(MP_ENOENT);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_2(vfs_cs_chdir_obj, vfs_cs_chdir);

static mp_obj_t vfs_cs_getcwd(mp_obj_t self_in) { return MP_OBJ_NEW_QSTR(MP_QSTR__slash_); }
static MP_DEFINE_CONST_FUN_OBJ_1(vfs_cs_getcwd_obj, vfs_cs_getcwd);

typedef struct {
    mp_obj_base_t base;
    mp_fun_1_t iternext;
    mp_fun_1_t finaliser;
    bool soTexto;
    void *pasta;
} vfs_cs_ilistdir_t;

static mp_obj_t vfs_cs_ilistdir_it_del(mp_obj_t self_in)
{
    vfs_cs_ilistdir_t *self = MP_OBJ_TO_PTR(self_in);
    if (self->pasta) cs_py_pasta_fechar(self->pasta);
    self->pasta = NULL;
    return mp_const_none;
}

static mp_obj_t vfs_cs_ilistdir_it_iternext(mp_obj_t self_in)
{
    vfs_cs_ilistdir_t *self = MP_OBJ_TO_PTR(self_in);
    char nome[128];
    bool pasta = false;
    int64_t tamanho = 0;
    if (!self->pasta || !cs_py_pasta_proximo(self->pasta, nome, sizeof(nome), &pasta, &tamanho)) {
        vfs_cs_ilistdir_it_del(self_in);
        return MP_OBJ_STOP_ITERATION;
    }
    mp_obj_tuple_t *t = MP_OBJ_TO_PTR(mp_obj_new_tuple(4, NULL));
    t->items[0] = self->soTexto ? mp_obj_new_str(nome, strlen(nome)) : mp_obj_new_bytes((const byte *)nome, strlen(nome));
    t->items[1] = MP_OBJ_NEW_SMALL_INT(pasta ? MP_S_IFDIR : MP_S_IFREG);
    t->items[2] = MP_OBJ_NEW_SMALL_INT(0);
    t->items[3] = mp_obj_new_int_from_ll(tamanho);
    return MP_OBJ_FROM_PTR(t);
}

static mp_obj_t vfs_cs_ilistdir(mp_obj_t self_in, mp_obj_t caminho)
{
    char buf[160];
    const char *c = caminhoDe(self_in, caminho, buf, sizeof(buf));
    void *p = cs_py_pasta_abrir(c);
    if (!p) mp_raise_OSError(MP_ENOENT);
    vfs_cs_ilistdir_t *it = mp_obj_malloc_with_finaliser(vfs_cs_ilistdir_t, &mp_type_polymorph_iter_with_finaliser);
    it->iternext = vfs_cs_ilistdir_it_iternext;
    it->finaliser = vfs_cs_ilistdir_it_del;
    it->soTexto = mp_obj_get_type(caminho) == &mp_type_str;
    it->pasta = p;
    return MP_OBJ_FROM_PTR(it);
}
static MP_DEFINE_CONST_FUN_OBJ_2(vfs_cs_ilistdir_obj, vfs_cs_ilistdir);

static mp_obj_t vfs_cs_mkdir(mp_obj_t self_in, mp_obj_t caminho)
{
    char buf[160];
    const char *c = caminhoDe(self_in, caminho, buf, sizeof(buf));
    if (cs_py_arq_existe(c)) mp_raise_OSError(MP_EEXIST);
    if (!cs_py_arq_criar_pasta(c)) mp_raise_OSError(MP_EIO);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_2(vfs_cs_mkdir_obj, vfs_cs_mkdir);

static mp_obj_t vfs_cs_remove(mp_obj_t self_in, mp_obj_t caminho)
{
    char buf[160];
    const char *c = caminhoDe(self_in, caminho, buf, sizeof(buf));
    if (!cs_py_arq_existe(c)) mp_raise_OSError(MP_ENOENT);
    if (!cs_py_arq_apagar(c)) mp_raise_OSError(MP_EIO);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_2(vfs_cs_remove_obj, vfs_cs_remove);

static mp_obj_t vfs_cs_rename(mp_obj_t self_in, mp_obj_t de, mp_obj_t para)
{
    char a[160], b[160];
    const char *x = caminhoDe(self_in, de, a, sizeof(a));
    const char *y = caminhoDe(self_in, para, b, sizeof(b));
    if (!cs_py_arq_existe(x)) mp_raise_OSError(MP_ENOENT);
    if (!cs_py_arq_renomear(x, y)) mp_raise_OSError(MP_EIO);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_3(vfs_cs_rename_obj, vfs_cs_rename);

static mp_obj_t vfs_cs_rmdir(mp_obj_t self_in, mp_obj_t caminho)
{
    char buf[160];
    const char *c = caminhoDe(self_in, caminho, buf, sizeof(buf));
    if (!cs_py_arq_eh_pasta(c)) mp_raise_OSError(MP_ENOENT);
    if (!cs_py_arq_apagar_pasta(c)) mp_raise_OSError(MP_EIO);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_2(vfs_cs_rmdir_obj, vfs_cs_rmdir);

static mp_obj_t vfs_cs_stat(mp_obj_t self_in, mp_obj_t caminho)
{
    char buf[160];
    const char *c = caminhoDe(self_in, caminho, buf, sizeof(buf));
    if (!cs_py_arq_existe(c)) mp_raise_OSError(MP_ENOENT);
    bool pasta = cs_py_arq_eh_pasta(c);
    mp_obj_tuple_t *t = MP_OBJ_TO_PTR(mp_obj_new_tuple(10, NULL));
    t->items[0] = MP_OBJ_NEW_SMALL_INT(pasta ? MP_S_IFDIR : MP_S_IFREG);
    for (int i = 1; i < 10; i++) t->items[i] = MP_OBJ_NEW_SMALL_INT(0);
    t->items[6] = mp_obj_new_int_from_ll(pasta ? 0 : cs_py_arq_tamanho(c));
    return MP_OBJ_FROM_PTR(t);
}
static MP_DEFINE_CONST_FUN_OBJ_2(vfs_cs_stat_obj, vfs_cs_stat);

static mp_obj_t vfs_cs_statvfs(mp_obj_t self_in, mp_obj_t caminho)
{
    /* A particao da ESP32 de 4 MB, com blocos de 4 KB. */
    mp_obj_tuple_t *t = MP_OBJ_TO_PTR(mp_obj_new_tuple(10, NULL));
    t->items[0] = MP_OBJ_NEW_SMALL_INT(4096);
    t->items[1] = MP_OBJ_NEW_SMALL_INT(4096);
    t->items[2] = MP_OBJ_NEW_SMALL_INT(512);
    t->items[3] = MP_OBJ_NEW_SMALL_INT(480);
    t->items[4] = MP_OBJ_NEW_SMALL_INT(480);
    for (int i = 5; i < 9; i++) t->items[i] = MP_OBJ_NEW_SMALL_INT(0);
    t->items[9] = MP_OBJ_NEW_SMALL_INT(255);
    return MP_OBJ_FROM_PTR(t);
}
static MP_DEFINE_CONST_FUN_OBJ_2(vfs_cs_statvfs_obj, vfs_cs_statvfs);

static const mp_rom_map_elem_t vfs_cs_locals_dict_table[] = {
    { MP_ROM_QSTR(MP_QSTR_mount), MP_ROM_PTR(&vfs_cs_mount_obj) },
    { MP_ROM_QSTR(MP_QSTR_umount), MP_ROM_PTR(&vfs_cs_umount_obj) },
    { MP_ROM_QSTR(MP_QSTR_open), MP_ROM_PTR(&vfs_cs_open_obj) },
    { MP_ROM_QSTR(MP_QSTR_chdir), MP_ROM_PTR(&vfs_cs_chdir_obj) },
    { MP_ROM_QSTR(MP_QSTR_getcwd), MP_ROM_PTR(&vfs_cs_getcwd_obj) },
    { MP_ROM_QSTR(MP_QSTR_ilistdir), MP_ROM_PTR(&vfs_cs_ilistdir_obj) },
    { MP_ROM_QSTR(MP_QSTR_mkdir), MP_ROM_PTR(&vfs_cs_mkdir_obj) },
    { MP_ROM_QSTR(MP_QSTR_remove), MP_ROM_PTR(&vfs_cs_remove_obj) },
    { MP_ROM_QSTR(MP_QSTR_rename), MP_ROM_PTR(&vfs_cs_rename_obj) },
    { MP_ROM_QSTR(MP_QSTR_rmdir), MP_ROM_PTR(&vfs_cs_rmdir_obj) },
    { MP_ROM_QSTR(MP_QSTR_stat), MP_ROM_PTR(&vfs_cs_stat_obj) },
    { MP_ROM_QSTR(MP_QSTR_statvfs), MP_ROM_PTR(&vfs_cs_statvfs_obj) },
};
static MP_DEFINE_CONST_DICT(vfs_cs_locals_dict, vfs_cs_locals_dict_table);

static mp_import_stat_t vfs_cs_import_stat(void *self_in, const char *path)
{
    vfs_cs_obj_t *self = self_in;
    char buf[160];
    snprintf(buf, sizeof(buf), "%s%s%s", self->prefixo, path[0] == '/' ? "" : "/", path);
    if (!cs_py_arq_existe(buf)) return MP_IMPORT_STAT_NO_EXIST;
    return cs_py_arq_eh_pasta(buf) ? MP_IMPORT_STAT_DIR : MP_IMPORT_STAT_FILE;
}

static const mp_vfs_proto_t vfs_cs_proto = {
    .import_stat = vfs_cs_import_stat,
};

MP_DEFINE_CONST_OBJ_TYPE(
    cs_vfs_type,
    MP_QSTR_Vfs,
    MP_TYPE_FLAG_NONE,
    protocol, &vfs_cs_proto,
    locals_dict, &vfs_cs_locals_dict
    );

static const vfs_cs_obj_t vfsDaFlash = {{&cs_vfs_type}, ""};
static const vfs_cs_obj_t vfsDoCartao = {{&cs_vfs_type}, "/sd"};

/* Liga a flash em "/", como o _boot.py do port da ESP32. */
void cs_vfs_montar(void)
{
    mp_vfs_mount_t *m = m_new_obj(mp_vfs_mount_t);
    m->str = "/";
    m->len = 1;
    m->obj = MP_OBJ_FROM_PTR(&vfsDaFlash);
    m->next = NULL;
    MP_STATE_VM(vfs_mount_table) = m;
    MP_STATE_VM(vfs_cur) = m;
}

/* ---- machine.SDCard(): o cartao da bancada, para o vfs.mount(sd, "/sd") ---- */
typedef struct {
    mp_obj_base_t base;
} machine_sdcard_obj_t;

static mp_obj_t machine_sdcard_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    if (!cs_py_cartao_montar()) mp_raise_msg(&mp_type_OSError, MP_ERROR_TEXT("no SD card"));
    return MP_OBJ_FROM_PTR(&vfsDoCartao);
}

MP_DEFINE_CONST_OBJ_TYPE(
    machine_sdcard_type,
    MP_QSTR_SDCard,
    MP_TYPE_FLAG_NONE,
    make_new, machine_sdcard_make_new
    );
