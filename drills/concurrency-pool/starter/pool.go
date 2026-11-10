// โจทย์บันได Concurrency ขั้น ③ · โค้ดตั้งต้น = ร่างที่ AI เขียนมา "ทำงานได้" แต่ผิดเงื่อนไข 3 ข้อ
// งานของคุณ: แก้ Run ให้ go test ./... ผ่าน (อย่าแก้ไฟล์ test)
//   1. ทำงานพร้อมกันไม่เกิน limit ตัว
//   2. ctx ถูกยกเลิก → หยุดรับงานใหม่ คืน ctx.Err() · งานที่ทำค้างอยู่ให้ทำจนจบ
//   3. คืนค่าแล้วต้องไม่มี goroutine ค้าง
package pool

import (
	"context"
	"sync"
)

// Run ส่งงานทุกชิ้นให้ work · คืน error ตัวแรกที่เจอ หรือ ctx.Err() ถ้าถูกยกเลิก
func Run(ctx context.Context, jobs []int, limit int, work func(context.Context, int) error) error {
	var wg sync.WaitGroup
	var first error
	var mu sync.Mutex
	for _, j := range jobs {
		wg.Add(1)
		go func(j int) {
			defer wg.Done()
			if err := work(ctx, j); err != nil {
				mu.Lock()
				if first == nil {
					first = err
				}
				mu.Unlock()
			}
		}(j)
	}
	wg.Wait()
	return first
}
